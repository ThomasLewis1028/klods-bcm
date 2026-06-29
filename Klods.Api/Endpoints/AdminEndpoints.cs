using System.IO.Compression;
using Klods.Database;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace Klods.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdmin(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").RequireAuthorization("Admin");

        group.MapPost("/import-colors", async (ImportData importer) =>
        {
            var ok = await importer.ImportColors();
            return ok ? Results.Ok() : Results.BadRequest("Color import failed.");
        });

        // Bulk catalog load. Accepts the Rebrickable CSVs as .csv, .csv.gz, or inside a .zip.
        // All-or-nothing: BulkImportService rejects the batch if any required file is missing.
        group.MapPost("/bulk-import", async (HttpRequest request, BulkImportService bulk, CancellationToken ct) =>
        {
            // Lift Kestrel's ~28 MB default request-body cap for this large upload only.
            var sizeFeature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = null;

            if (!request.HasFormContentType) return Results.BadRequest("Expected a multipart file upload.");
            var form = await request.ReadFormAsync(ct);

            DateTime? snapshot = DateTime.TryParse(form["snapshotDate"], out var d) ? d.ToUniversalTime() : null;

            var files = new Dictionary<string, Stream>(StringComparer.OrdinalIgnoreCase);
            var disposables = new List<IDisposable>();
            try
            {
                foreach (var file in form.Files)
                {
                    if (file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        var archive = new ZipArchive(file.OpenReadStream(), ZipArchiveMode.Read);
                        disposables.Add(archive);
                        foreach (var entry in archive.Entries)
                        {
                            if (entry.Length == 0 || LogicalName(entry.Name) is not { } name) continue;
                            files[name] = entry.Open();
                        }
                    }
                    else if (LogicalName(file.FileName) is { } name)
                    {
                        Stream s = file.OpenReadStream();
                        if (file.FileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                        {
                            s = new GZipStream(s, CompressionMode.Decompress);
                            disposables.Add(s);
                        }
                        files[name] = s;
                    }
                }

                var result = await bulk.ImportAsync(files, snapshot, ct);
                return result.Status == "Success"
                    ? Results.Ok(new BulkImportResultDto(result.Status, result.Notes, result.ImportedAt))
                    : Results.BadRequest(new BulkImportResultDto(result.Status, result.Notes, result.ImportedAt));
            }
            finally
            {
                foreach (var dispose in disposables) dispose.Dispose();
            }
        }).DisableAntiforgery();

        // Most recent catalog loads, for the admin "last refreshed" display.
        group.MapGet("/catalog-imports", async (IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();
            var rows = await db.Set<CatalogImport>().AsNoTracking()
                .OrderByDescending(c => c.ImportedAt)
                .Take(10)
                .Select(c => new CatalogImportDto(c.ImportedAt, c.SnapshotDate, c.Source, c.Status, c.Notes))
                .ToListAsync();
            return Results.Ok(rows);
        });

        // RSS auto-update toggle + manual poll.
        group.MapGet("/rss-settings", async (SettingsService settings) =>
            Results.Ok(new RssSettingsDto(await settings.GetBoolAsync(RssUpdateService.EnabledKey))));

        group.MapPut("/rss-settings", async (RssSettingsDto req, SettingsService settings) =>
        {
            await settings.SetAsync(RssUpdateService.EnabledKey, req.Enabled ? "true" : "false");
            return Results.Ok();
        });

        group.MapPost("/rss-poll", async (RssUpdateService rss, CancellationToken ct) =>
        {
            var result = await rss.PollAsync(ct: ct);
            return Results.Ok(new CatalogImportDto(result.ImportedAt, result.SnapshotDate, result.Source, result.Status, result.Notes));
        });

        group.MapGet("/users", async (IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();
            var users = await db.Users.AsNoTracking()
                .OrderBy(u => u.UserName)
                .Select(u => new UserDto(u.UserId, u.UserName, u.Role, u.ProfilePictureUrl))
                .ToListAsync();
            return Results.Ok(users);
        });


        group.MapPatch("/users/{userId:int}/role", async (
            int userId, SetRoleRequest req, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            if (req.Role != "Admin" && req.Role != "User")
                return Results.BadRequest("Role must be 'Admin' or 'User'.");
            await using var db = dbFactory.CreateDbContext();
            var rows = await db.Users.Where(u => u.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, req.Role));
            return rows > 0 ? Results.Ok() : Results.NotFound();
        });
    }

    // "inventory_parts.csv.gz" -> "inventory_parts"; ignores non-CSV entries.
    private static string? LogicalName(string fileName)
    {
        var n = Path.GetFileName(fileName).ToLowerInvariant();
        if (n.EndsWith(".csv.gz")) return n[..^7];
        if (n.EndsWith(".csv")) return n[..^4];
        if (n.EndsWith(".gz")) return n[..^3];
        return null;
    }

    public record UserDto(int UserId, string UserName, string Role, string? ProfilePictureUrl);
    public record SetRoleRequest(string Role);
    public record BulkImportResultDto(string Status, string? Notes, DateTime ImportedAt);
    public record CatalogImportDto(DateTime ImportedAt, DateTime? SnapshotDate, string Source, string Status, string? Notes);
    public record RssSettingsDto(bool Enabled);
}
