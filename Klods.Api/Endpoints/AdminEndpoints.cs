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

        // RSS auto-update settings + manual poll.
        group.MapGet("/rss-settings", async (SettingsService settings) =>
            Results.Ok(new RssSettingsDto(
                await settings.GetBoolAsync(RssUpdateService.EnabledKey),
                await settings.GetAsync(RssUpdateService.CronKey) ?? RssUpdateService.DefaultCron,
                await settings.GetAsync(RssUpdateService.TimezoneKey) ?? RssUpdateService.DefaultTimezone,
                await settings.GetIntAsync(RssUpdateService.MaxImportsKey, RssUpdateService.DefaultMaxImports))));

        group.MapPut("/rss-settings", async (RssSettingsDto req, SettingsService settings) =>
        {
            var cron = (req.Cron ?? "").Trim();
            if (NCrontab.CrontabSchedule.TryParse(cron) is null)
                return Results.BadRequest("Invalid cron expression (expected 5 fields, e.g. '0 3 * * *').");
            var tz = (req.Timezone ?? "").Trim();
            if (!CronHelper.IsValidTimeZone(tz))
                return Results.BadRequest($"Unknown timezone '{tz}'.");
            var max = Math.Clamp(req.MaxImports, 1, 500);

            await settings.SetAsync(RssUpdateService.EnabledKey, req.Enabled ? "true" : "false");
            await settings.SetAsync(RssUpdateService.CronKey, cron);
            await settings.SetAsync(RssUpdateService.TimezoneKey, tz);
            await settings.SetAsync(RssUpdateService.MaxImportsKey, max.ToString());
            return Results.Ok();
        });

        // Available timezones for the schedule picker.
        group.MapGet("/timezones", () =>
            Results.Ok(TimeZoneInfo.GetSystemTimeZones()
                .Select(t => new TimezoneDto(t.Id, t.DisplayName))
                .ToList()));

        // Live preview: the next few runs of a cron expression in a given timezone (also validates).
        group.MapGet("/cron-preview", (string cron, string? tz) =>
        {
            var next = CronHelper.NextOccurrencesLocal((cron ?? "").Trim(), CronHelper.ResolveTimeZone(tz), 5);
            return Results.Ok(new CronPreviewDto(
                next.Count > 0,
                next.Select(d => d.ToString("ddd, dd MMM yyyy HH:mm")).ToList()));
        });

        group.MapPost("/rss-poll", async (RssUpdateService rss, SettingsService settings, CancellationToken ct) =>
        {
            var max = await settings.GetIntAsync(RssUpdateService.MaxImportsKey, RssUpdateService.DefaultMaxImports, ct);
            var result = await rss.PollAsync(max, ct);
            return Results.Ok(new CatalogImportDto(result.ImportedAt, result.SnapshotDate, result.Source, result.Status, result.Notes));
        });

        // Set-update auto-refresh settings + manual poll (re-imports locally-held sets changed upstream).
        group.MapGet("/set-update-settings", async (SettingsService settings) =>
            Results.Ok(new SetUpdateSettingsDto(
                await settings.GetBoolAsync(SetUpdateService.EnabledKey),
                await settings.GetAsync(SetUpdateService.CronKey) ?? SetUpdateService.DefaultCron,
                await settings.GetAsync(SetUpdateService.TimezoneKey) ?? SetUpdateService.DefaultTimezone,
                await settings.GetIntAsync(SetUpdateService.MaxReimportsKey, SetUpdateService.DefaultMaxReimports))));

        group.MapPut("/set-update-settings", async (SetUpdateSettingsDto req, SettingsService settings) =>
        {
            var cron = (req.Cron ?? "").Trim();
            if (NCrontab.CrontabSchedule.TryParse(cron) is null)
                return Results.BadRequest("Invalid cron expression (expected 5 fields, e.g. '0 3 * * *').");
            var tz = (req.Timezone ?? "").Trim();
            if (!CronHelper.IsValidTimeZone(tz))
                return Results.BadRequest($"Unknown timezone '{tz}'.");
            var max = Math.Clamp(req.MaxReimports, 1, 1000);

            await settings.SetAsync(SetUpdateService.EnabledKey, req.Enabled ? "true" : "false");
            await settings.SetAsync(SetUpdateService.CronKey, cron);
            await settings.SetAsync(SetUpdateService.TimezoneKey, tz);
            await settings.SetAsync(SetUpdateService.MaxReimportsKey, max.ToString());
            return Results.Ok();
        });

        group.MapPost("/set-update-poll", async (SetUpdateService svc, SettingsService settings, CancellationToken ct) =>
        {
            var max = await settings.GetIntAsync(SetUpdateService.MaxReimportsKey, SetUpdateService.DefaultMaxReimports, ct);
            var result = await svc.PollAsync(max, ct);
            return Results.Ok(new CatalogImportDto(result.ImportedAt, result.SnapshotDate, result.Source, result.Status, result.Notes));
        });

        // Theme visibility: every theme in the catalog with its set count and whether it's hidden
        // from the set browse/search surfaces.
        group.MapGet("/theme-visibility", async (IDbContextFactory<InventoryContext> dbFactory, SettingsService settings) =>
        {
            await using var db = dbFactory.CreateDbContext();

            var setCounts = await db.Set<Set>().AsNoTracking()
                .Where(s => s.ThemeId != null)
                .GroupBy(s => s.ThemeId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count);

            var themes = await db.Set<Theme>().AsNoTracking().ToListAsync();
            // Raw picks (not expanded) so the checkboxes reflect exactly what the admin chose.
            var hidden = (await CatalogSettings.GetHiddenThemeIdsAsync(settings)).ToHashSet();

            var result = themes
                .Select(t => new ThemeVisibilityDto(
                    t.Id, t.Name, t.ParentId, setCounts.GetValueOrDefault(t.Id, 0), hidden.Contains(t.Id)))
                .OrderBy(t => t.Name)
                .ToList();
            return Results.Ok(result);
        });

        group.MapPut("/theme-visibility", async (ThemeVisibilityUpdateDto req, SettingsService settings) =>
        {
            await CatalogSettings.SetHiddenThemeIdsAsync(settings, req.HiddenThemeIds ?? []);
            return Results.Ok();
        });

        group.MapGet("/registration-settings", async (SettingsService settings) =>
            Results.Ok(new RegistrationSettingsDto(await settings.GetBoolAsync(AuthEndpoints.AutoApproveKey, true))));

        group.MapPut("/registration-settings", async (RegistrationSettingsDto req, SettingsService settings) =>
        {
            await settings.SetAsync(AuthEndpoints.AutoApproveKey, req.AutoApprove ? "true" : "false");
            return Results.Ok();
        });

        group.MapGet("/users", async (IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();
            var users = await db.Users.AsNoTracking()
                .OrderBy(u => u.UserName)
                .Select(u => new UserDto(u.UserId, u.UserName, u.Role, u.ProfilePictureUrl, u.Status))
                .ToListAsync();
            return Results.Ok(users);
        });

        group.MapPatch("/users/{userId:int}/status", async (
            int userId, SetStatusRequest req, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            if (req.Status != "Active" && req.Status != "Pending")
                return Results.BadRequest("Status must be 'Active' or 'Pending'.");
            await using var db = dbFactory.CreateDbContext();
            var rows = await db.Users.Where(u => u.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, req.Status));
            return rows > 0 ? Results.Ok() : Results.NotFound();
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

    public record UserDto(int UserId, string UserName, string Role, string? ProfilePictureUrl, string Status);
    public record SetRoleRequest(string Role);
    public record SetStatusRequest(string Status);
    public record RegistrationSettingsDto(bool AutoApprove);
    public record BulkImportResultDto(string Status, string? Notes, DateTime ImportedAt);
    public record CatalogImportDto(DateTime ImportedAt, DateTime? SnapshotDate, string Source, string Status, string? Notes);
    public record RssSettingsDto(bool Enabled, string Cron, string Timezone, int MaxImports);
    public record SetUpdateSettingsDto(bool Enabled, string Cron, string Timezone, int MaxReimports);
    public record ThemeVisibilityDto(int Id, string Name, int? ParentId, int SetCount, bool Hidden);
    public record ThemeVisibilityUpdateDto(int[] HiddenThemeIds);
    public record TimezoneDto(string Id, string DisplayName);
    public record CronPreviewDto(bool Valid, List<string> Next);
}
