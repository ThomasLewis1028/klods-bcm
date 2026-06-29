using System.Globalization;
using Klods;
using Klods.Services;
using NCrontab;

namespace Klods.Api;

/// <summary>
/// Cron-scheduled background poll of the Rebrickable RSS feed. Ticks every minute and runs a poll
/// when the configured cron schedule (UTC) is due — but only while the admin toggle is on.
/// </summary>
public class RssBackgroundService(IServiceScopeFactory scopeFactory, ILogger<RssBackgroundService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let startup + migrations settle before the first tick.
        try { await Task.Delay(Tick, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "RSS scheduler tick failed"); }

            try { await Task.Delay(Tick, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();

        if (!await settings.GetBoolAsync(RssUpdateService.EnabledKey, ct: ct)) return;

        var cron = await settings.GetAsync(RssUpdateService.CronKey, ct) ?? RssUpdateService.DefaultCron;
        var schedule = CrontabSchedule.TryParse(cron);
        if (schedule is null)
        {
            logger.LogWarning("Invalid RSS cron expression '{Cron}'; skipping", cron);
            return;
        }

        var tz = CronHelper.ResolveTimeZone(await settings.GetAsync(RssUpdateService.TimezoneKey, ct));
        var nowUtc = DateTime.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
        var lastStr = await settings.GetAsync(RssUpdateService.LastPollAtKey, ct);

        // First run (or unparseable): anchor now so the first poll lands at the next scheduled time.
        if (lastStr is null || !DateTime.TryParse(lastStr, null, DateTimeStyles.RoundtripKind, out var lastPollUtc))
        {
            await settings.SetAsync(RssUpdateService.LastPollAtKey, nowUtc.ToString("o"), ct);
            return;
        }

        // Evaluate the schedule in the configured timezone.
        var lastLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(lastPollUtc, DateTimeKind.Utc), tz);
        if (schedule.GetNextOccurrence(lastLocal) > nowLocal) return; // not due yet

        var max = await settings.GetIntAsync(RssUpdateService.MaxImportsKey, RssUpdateService.DefaultMaxImports, ct);
        var rss = scope.ServiceProvider.GetRequiredService<RssUpdateService>();
        await rss.PollAsync(max, ct);
        await settings.SetAsync(RssUpdateService.LastPollAtKey, nowUtc.ToString("o"), ct);
    }
}
