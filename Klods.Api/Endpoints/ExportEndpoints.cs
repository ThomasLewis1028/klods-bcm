using System.Text;
using ClosedXML.Excel;
using Klods;
using Klods.Database;
using Klods.Services;
using Microsoft.EntityFrameworkCore;

namespace Klods.Api.Endpoints;

public static class ExportEndpoints
{
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static void MapExport(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/export").RequireAuthorization();

        // One row per (owned set copy × BOM part), as CSV or XLSX. onlyMissing keeps only rows still
        // short; XLSX additionally embeds part images and tints the colour cell to the brick's colour.
        group.MapGet("/parts", async (
            bool? onlyMissing, string? format, HttpContext http,
            IDbContextFactory<InventoryContext> dbFactory, ImageStorageService imageStorage, CancellationToken ct) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            var rows = await BuildRowsAsync(db, userId, onlyMissing == true);

            if (string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase))
                return Results.File(await BuildXlsxAsync(rows, imageStorage, ct), XlsxContentType, "parts.xlsx");

            return Results.Text(BuildCsv(rows), "text/csv", Encoding.UTF8);
        });
    }

    private record ExportRow(
        string SetId, string SetName, int Copy, string PartNum, string PartName,
        string ColorName, string HexColor, int Required, int InSet, int Substituted, int Missing, string? ImageUrl);

    private static async Task<List<ExportRow>> BuildRowsAsync(InventoryContext db, int userId, bool onlyMissing)
    {
        var copies = await db.Set<SetOwned>().AsNoTracking()
            .Where(so => so.UserId == userId)
            .Select(so => new { so.SetId, so.SetIndex })
            .ToListAsync();

        var setIds = copies.Select(c => c.SetId).Distinct().ToList();

        var setNames = (await db.Set<Set>().AsNoTracking()
                .Where(s => setIds.Contains(s.SetId))
                .Select(s => new { s.SetId, s.Name }).ToListAsync())
            .ToDictionary(s => s.SetId, s => s.Name);

        var setBricks = await db.Set<SetBrick>().AsNoTracking()
            .Where(sb => setIds.Contains(sb.SetId))
            .Select(sb => new { sb.SetId, sb.PartNum, sb.ColorId, sb.Count })
            .ToListAsync();

        var partNums = setBricks.Select(sb => sb.PartNum).ToHashSet();
        var brickDict = (await db.Set<Brick>().AsNoTracking()
                .Where(b => partNums.Contains(b.PartNum))
                .ToListAsync())
            .ToDictionary(b => (b.PartNum, b.ColorId ?? ""));

        var setStock = (await db.Set<SetBrickOwned>().AsNoTracking()
                .Where(sbo => sbo.UserId == userId && setIds.Contains(sbo.SetId))
                .ToListAsync())
            .ToDictionary(x => (x.SetId, x.SetIndex, x.PartNum, x.ColorId), x => x.Stock);

        var subDict = (await db.Set<SetBrickSubstitution>().AsNoTracking()
                .Where(s => s.UserId == userId && setIds.Contains(s.SetId))
                .Select(s => new { s.SetId, s.SetIndex, s.ReqPartNum, s.ReqColorId, s.Count })
                .ToListAsync())
            .GroupBy(x => (x.SetId, x.SetIndex, x.ReqPartNum, x.ReqColorId))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

        var bomBySet = setBricks.GroupBy(sb => sb.SetId).ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<ExportRow>();
        foreach (var copy in copies.OrderBy(c => c.SetId).ThenBy(c => c.SetIndex))
        {
            if (!bomBySet.TryGetValue(copy.SetId, out var bom)) continue;
            foreach (var part in bom.OrderBy(p => p.PartNum).ThenBy(p => p.ColorId))
            {
                if (!brickDict.TryGetValue((part.PartNum, part.ColorId), out var brick)) continue;

                var inSet = setStock.GetValueOrDefault((copy.SetId, copy.SetIndex, part.PartNum, part.ColorId), 0);
                var subbed = subDict.GetValueOrDefault((copy.SetId, copy.SetIndex, part.PartNum, part.ColorId), 0);
                var placed = Math.Min(inSet, part.Count);
                var subFill = Math.Min(subbed, part.Count - placed);
                var missing = part.Count - placed - subFill;

                if (onlyMissing && missing <= 0) continue;

                rows.Add(new ExportRow(
                    copy.SetId, setNames.GetValueOrDefault(copy.SetId, ""), copy.SetIndex,
                    part.PartNum, brick.Name, brick.ColorName ?? "", brick.HexColor ?? "",
                    part.Count, inSet, subbed, missing, brick.PartImg));
            }
        }
        return rows;
    }

    private static readonly string[] Headers =
        ["Set ID", "Set Name", "Copy", "Part Number", "Part Name", "Color", "Color Hex",
         "Required", "In Set", "Substituted", "Missing", "Image"];

    private static string BuildCsv(List<ExportRow> rows)
    {
        var csv = new StringBuilder();
        csv.Append('﻿'); // UTF-8 BOM so Excel reads accented names correctly
        AppendCsvRow(csv, Headers);
        foreach (var r in rows)
            AppendCsvRow(csv,
                r.SetId, r.SetName, r.Copy.ToString(), r.PartNum, r.PartName,
                r.ColorName, r.HexColor, r.Required.ToString(), r.InSet.ToString(),
                r.Substituted.ToString(), r.Missing.ToString(), r.ImageUrl ?? "");
        return csv.ToString();
    }

    private static async Task<byte[]> BuildXlsxAsync(List<ExportRow> rows, ImageStorageService imageStorage, CancellationToken ct)
    {
        // Image column is dropped (images are embedded instead of a URL); Color Hex is dropped too
        // since the Color cell itself is tinted. Header order tracks that.
        string[] headers = ["Set ID", "Set Name", "Copy", "Part Number", "Part Name", "Color",
                            "Required", "In Set", "Substituted", "Missing", "Image"];
        const int colorCol = 6;
        const int imageCol = 11;

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Parts");

        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
        }
        ws.SheetView.FreezeRows(1);

        // Fetch each distinct part image once (best-effort — only Rebrickable-CDN images resolve here).
        var imageBytes = new Dictionary<string, byte[]>();
        foreach (var url in rows.Select(r => r.ImageUrl).Where(u => !string.IsNullOrEmpty(u)).Distinct())
        {
            var img = await imageStorage.GetThroughCacheAsync(url!, ct);
            if (img is { Bytes.Length: > 0 }) imageBytes[url!] = img.Value.Bytes;
        }

        var streams = new List<MemoryStream>();
        var r = 2;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.SetId;
            ws.Cell(r, 2).Value = row.SetName;
            ws.Cell(r, 3).Value = row.Copy;
            ws.Cell(r, 4).Value = row.PartNum;
            ws.Cell(r, 5).Value = row.PartName;

            var colorCell = ws.Cell(r, colorCol);
            colorCell.Value = row.ColorName;
            var hex = row.HexColor.TrimStart('#');
            if (hex.Length == 6)
            {
                try
                {
                    colorCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#" + hex);
                    colorCell.Style.Font.FontColor = ColorHelper.IsDark(hex) ? XLColor.White : XLColor.FromArgb(0x20, 0x20, 0x20);
                }
                catch { /* malformed hex — leave the cell unstyled */ }
            }

            ws.Cell(r, 7).Value = row.Required;
            ws.Cell(r, 8).Value = row.InSet;
            ws.Cell(r, 9).Value = row.Substituted;
            ws.Cell(r, 10).Value = row.Missing;

            if (row.ImageUrl != null && imageBytes.TryGetValue(row.ImageUrl, out var bytes))
            {
                var ms = new MemoryStream(bytes);
                streams.Add(ms);
                ws.AddPicture(ms, $"p{r}").MoveTo(ws.Cell(r, imageCol), 2, 2).WithSize(46, 46);
                ws.Row(r).Height = 38;
            }
            r++;
        }

        ws.Columns(1, 10).AdjustToContents();
        ws.Column(imageCol).Width = 8;

        using var outMs = new MemoryStream();
        wb.SaveAs(outMs);
        foreach (var s in streams) s.Dispose();
        return outMs.ToArray();
    }

    private static void AppendCsvRow(StringBuilder sb, params string[] fields)
    {
        for (var i = 0; i < fields.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(CsvField(fields[i]));
        }
        sb.Append("\r\n");
    }

    private static string CsvField(string field) =>
        field.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? "\"" + field.Replace("\"", "\"\"") + "\""
            : field;
}
