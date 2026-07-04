using System.Globalization;
using Klods.Database;
using Klods.Services;
using Microsoft.EntityFrameworkCore;

namespace Klods;

/// <summary>
/// Polls Rebrickable's "newest sets" RSS feed and imports any sets added since the last poll
/// (or since the last bulk snapshot, on first run) that aren't already in the catalog.
/// Imports are capped per poll to stay friendly with the rate-limited API. Each poll is recorded
/// as a <see cref="CatalogImport"/> (Source = "RssPoll") for display.
/// </summary>
public class RssUpdateService(
    IDbContextFactory<InventoryContext> contextFactory,
    RebrickableApi rebrickable,
    ImportData importer,
    SettingsService settings,
    ILogger<RssUpdateService> logger)
{
    public const string EnabledKey = "rss.enabled";
    public const string CronKey = "rss.cron";
    public const string TimezoneKey = "rss.timezone";
    public const string MaxImportsKey = "rss.maxImports";
    public const string LastPollAtKey = "rss.lastPollAt";
    public const string DefaultCron = "0 * * * *"; // hourly
    public const string DefaultTimezone = "UTC";
    public const int DefaultMaxImports = 25;
    private const string LastPubDateKey = "rss.lastPubDate";

    public async Task<CatalogImport> PollAsync(int maxImports = DefaultMaxImports, CancellationToken ct = default)
    {
        List<RssSetItem> items;
        try
        {
            items = await rebrickable.GetRecentSetsFromRssAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RSS feed fetch failed");
            return await RecordAsync("Failed", $"Feed fetch failed: {ex.Message}", ct);
        }

        var baseline = await GetBaselineAsync(ct);
        var newItems = items.Where(i => i.PubDate > baseline).OrderBy(i => i.PubDate).ToList();

        if (newItems.Count == 0)
            return await RecordAsync("Success", "No new sets in feed.", ct);

        var imported = new List<string>();
        var alreadyInCatalog = 0;
        var failed = new List<string>();
        var processedThrough = baseline;
        // Once a set fails transiently we stop advancing the watermark, so it (and everything newer)
        // is retried next poll instead of being silently skipped forever.
        var watermarkFrozen = false;
        var capReached = false;

        await using (var db = await contextFactory.CreateDbContextAsync(ct))
        {
            foreach (var item in newItems)
            {
                if (imported.Count >= maxImports) { capReached = true; break; }

                bool handled;
                if (await db.Set<Set>().AnyAsync(s => s.SetId == item.SetNum, ct))
                {
                    alreadyInCatalog++;
                    handled = true;
                }
                else
                {
                    try
                    {
                        await importer.ImportAll([item.SetNum], throwOnError: true);
                        imported.Add(item.SetNum);
                        handled = true;
                    }
                    catch (RebrickableApiException ex) when (ex.StatusCode == 404)
                    {
                        // The feed listed a set the sets API doesn't have — retrying won't help.
                        logger.LogWarning("RSS: set {SetNum} not found on the sets API (404); skipping permanently", item.SetNum);
                        handled = true;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "RSS import of {SetNum} failed; holding watermark to retry next poll", item.SetNum);
                        failed.Add(item.SetNum);
                        handled = false;
                    }
                }

                if (!handled) watermarkFrozen = true;
                else if (!watermarkFrozen) processedThrough = item.PubDate;
            }
        }

        await settings.SetAsync(LastPubDateKey, processedThrough.ToString("o"), ct);

        var segments = new List<string>
        {
            imported.Count > 0 ? $"Imported {imported.Count}: {string.Join(", ", imported)}" : "Imported 0"
        };
        if (alreadyInCatalog > 0) segments.Add($"{alreadyInCatalog} already in catalog");
        if (failed.Count > 0) segments.Add($"{failed.Count} failed, will retry: {string.Join(", ", failed)}");
        var notes = string.Join("; ", segments);
        if (capReached) notes += $"; import cap ({maxImports}) reached, remaining sets import next poll";

        logger.LogInformation("RSS poll: {Notes}", notes);
        return await RecordAsync("Success", notes, ct);
    }

    private async Task<DateTime> GetBaselineAsync(CancellationToken ct)
    {
        var last = await settings.GetAsync(LastPubDateKey, ct);
        if (last != null && DateTime.TryParse(last, null, DateTimeStyles.RoundtripKind, out var d))
            return d;

        // First run: catch up from the latest bulk snapshot if we have one; otherwise only go forward.
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var bulkSnapshot = await db.Set<CatalogImport>().AsNoTracking()
            .Where(c => c.Source == "BulkUpload" && c.SnapshotDate != null)
            .OrderByDescending(c => c.ImportedAt)
            .Select(c => c.SnapshotDate)
            .FirstOrDefaultAsync(ct);
        return bulkSnapshot ?? DateTime.UtcNow;
    }

    private async Task<CatalogImport> RecordAsync(string status, string notes, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var record = new CatalogImport
        {
            ImportedAt = DateTime.UtcNow,
            Source = "RssPoll",
            Status = status,
            Notes = notes,
        };
        db.Set<CatalogImport>().Add(record);
        await db.SaveChangesAsync(ct);
        return record;
    }
}
