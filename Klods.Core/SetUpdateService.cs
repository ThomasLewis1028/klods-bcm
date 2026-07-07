using System.Globalization;
using Klods.Database;
using Klods.Services;
using Microsoft.EntityFrameworkCore;

namespace Klods;

/// <summary>
/// Polls Rebrickable's sets list (ordered by last_modified_dt) and re-imports any set we already hold
/// locally that has changed upstream since the last poll. Re-import runs through the deletion-aware
/// <see cref="ImportData.ImportAll"/>, so a set that dropped a part gets that part removed and any owned
/// stock returned to loose inventory. Sets we don't hold are ignored — the catalog only needs to be
/// current for what's actually in it. Each poll is recorded as a <see cref="CatalogImport"/>
/// (Source = "SetUpdatePoll") for the admin history.
/// </summary>
public class SetUpdateService(
    IDbContextFactory<InventoryContext> contextFactory,
    RebrickableApi rebrickable,
    ImportData importer,
    SettingsService settings,
    NotificationService notifications,
    ILogger<SetUpdateService> logger)
{
    public const string EnabledKey = "setUpdate.enabled";
    public const string CronKey = "setUpdate.cron";
    public const string TimezoneKey = "setUpdate.timezone";
    public const string MaxReimportsKey = "setUpdate.maxReimports";
    public const string LastPollAtKey = "setUpdate.lastPollAt";
    public const string DefaultCron = "0 3 * * *"; // 3am daily
    public const string DefaultTimezone = "UTC";
    public const int DefaultMaxReimports = 100;
    private const string WatermarkKey = "setUpdate.watermark";

    public async Task<CatalogImport> PollAsync(int maxReimports = DefaultMaxReimports, CancellationToken ct = default)
    {
        var watermark = await GetWatermarkAsync(ct);

        List<ModifiedSetItem> modified;
        try
        {
            modified = await rebrickable.GetSetsModifiedSince(watermark, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Set-update feed fetch failed");
            return await RecordAsync("Failed", $"Feed fetch failed: {ex.Message}", ct);
        }

        if (modified.Count == 0)
            return await RecordAsync("Success", "No catalog changes since last poll.", ct);

        // Oldest change first so the watermark advances monotonically and a cap leaves a clean resume point.
        modified = modified.OrderBy(m => m.LastModified).ToList();

        // Only sets already in our catalog are worth re-importing.
        HashSet<string> localSets;
        await using (var db = await contextFactory.CreateDbContextAsync(ct))
        {
            var candidateIds = modified.Select(m => m.SetNum).ToList();
            localSets = (await db.Set<Set>().AsNoTracking()
                    .Where(s => candidateIds.Contains(s.SetId))
                    .Select(s => s.SetId)
                    .ToListAsync(ct))
                .ToHashSet();
        }

        var reimported = new List<string>();
        var failed = new List<string>();
        var notInCatalog = 0;
        var processedThrough = watermark;
        // Once a set fails transiently we stop advancing the watermark, so it (and everything newer) is
        // retried next poll instead of being skipped forever.
        var watermarkFrozen = false;
        var capReached = false;

        foreach (var item in modified)
        {
            if (!localSets.Contains(item.SetNum))
            {
                notInCatalog++;
                if (!watermarkFrozen) processedThrough = item.LastModified;
                continue;
            }

            if (reimported.Count >= maxReimports) { capReached = true; break; }

            try
            {
                var changes = new List<PartChange>();
                await importer.ImportAll([item.SetNum], throwOnError: true, changes);
                reimported.Add(item.SetNum);
                if (changes.Count > 0)
                    await notifications.WriteForSetChangeAsync(item.SetNum, changes, DateTime.UtcNow, ct);
                if (!watermarkFrozen) processedThrough = item.LastModified;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Set-update re-import of {SetNum} failed; holding watermark to retry next poll", item.SetNum);
                failed.Add(item.SetNum);
                watermarkFrozen = true;
            }
        }

        await settings.SetAsync(WatermarkKey, processedThrough.ToString("o"), ct);
        await notifications.CleanupAsync(ct);

        var segments = new List<string>
        {
            reimported.Count > 0 ? $"Re-imported {reimported.Count}: {string.Join(", ", reimported)}" : "Re-imported 0",
            $"{modified.Count} changed upstream, {notInCatalog} not in catalog",
        };
        if (failed.Count > 0) segments.Add($"{failed.Count} failed, will retry: {string.Join(", ", failed)}");
        var notes = string.Join("; ", segments);
        if (capReached) notes += $"; re-import cap ({maxReimports}) reached, remaining sets continue next poll";

        logger.LogInformation("Set-update poll: {Notes}", notes);
        return await RecordAsync("Success", notes, ct);
    }

    private async Task<DateTime> GetWatermarkAsync(CancellationToken ct)
    {
        var stored = await settings.GetAsync(WatermarkKey, ct);
        if (stored != null && DateTime.TryParse(stored, null, DateTimeStyles.RoundtripKind, out var d))
            return d.ToUniversalTime();

        // First run: anchor at the newest last_modified we already hold, so we don't treat the entire
        // back-catalog as "changed". If we hold no sets yet, only look forward from now.
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var newestHeld = await db.Set<Set>().AsNoTracking()
            .OrderByDescending(s => s.DateModified)
            .Select(s => (DateTime?)s.DateModified)
            .FirstOrDefaultAsync(ct);
        return newestHeld ?? DateTime.UtcNow;
    }

    private async Task<CatalogImport> RecordAsync(string status, string notes, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var record = new CatalogImport
        {
            ImportedAt = DateTime.UtcNow,
            Source = "SetUpdatePoll",
            Status = status,
            Notes = notes,
        };
        db.Set<CatalogImport>().Add(record);
        await db.SaveChangesAsync(ct);
        return record;
    }
}
