using System.Text;
using Klods.Database;
using Microsoft.EntityFrameworkCore;

namespace Klods.Api.Endpoints;

public static class ExportEndpoints
{
    public static void MapExport(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/export").RequireAuthorization();

        // One CSV row per (owned set copy × BOM part). onlyMissing keeps only rows still short.
        // Missing mirrors the completeness rule: real parts count first (capped at required),
        // then substitutions fill the remainder.
        group.MapGet("/parts", async (
            bool? onlyMissing, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

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

            var csv = new StringBuilder();
            csv.Append('﻿'); // UTF-8 BOM so Excel reads accented names correctly
            AppendRow(csv,
                "Set ID", "Set Name", "Copy", "Part Number", "Part Name", "Color", "Color Hex",
                "Required", "In Set", "Substituted", "Missing", "Image URL");

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

                    if (onlyMissing == true && missing <= 0) continue;

                    AppendRow(csv,
                        copy.SetId,
                        setNames.GetValueOrDefault(copy.SetId, ""),
                        copy.SetIndex.ToString(),
                        part.PartNum,
                        brick.Name,
                        brick.ColorName ?? "",
                        brick.HexColor ?? "",
                        part.Count.ToString(),
                        inSet.ToString(),
                        subbed.ToString(),
                        missing.ToString(),
                        brick.PartImg ?? "");
                }
            }

            return Results.Text(csv.ToString(), "text/csv", Encoding.UTF8);
        });
    }

    private static void AppendRow(StringBuilder sb, params string[] fields)
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
