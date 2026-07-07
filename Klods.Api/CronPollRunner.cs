using System.Globalization;
using Klods.Services;
using NCrontab;

namespace Klods.Api;

/// <summary>
/// Shared cron gate for the background pollers. Given a set of settings keys, decides whether the
/// configured schedule (evaluated in the configured timezone) is due and, if so, runs the poll and
/// advances the last-poll marker. Keeps <see cref="RssBackgroundService"/> and
/// <see cref="SetUpdateBackgroundService"/> from carrying duplicate scheduling logic.
/// </summary>
public static class CronPollRunner
{
    public static async Task RunIfDueAsync(
        SettingsService settings,
        ILogger logger,
        string label,
        string enabledKey,
        string cronKey,
        string timezoneKey,
        string lastPollKey,
        string defaultCron,
        Func<CancellationToken, Task> poll,
        CancellationToken ct)
    {
        if (!await settings.GetBoolAsync(enabledKey, ct: ct)) return;

        var cron = await settings.GetAsync(cronKey, ct) ?? defaultCron;
        var schedule = CrontabSchedule.TryParse(cron);
        if (schedule is null)
        {
            logger.LogWarning("Invalid {Label} cron expression '{Cron}'; skipping", label, cron);
            return;
        }

        var tz = CronHelper.ResolveTimeZone(await settings.GetAsync(timezoneKey, ct));
        var nowUtc = DateTime.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
        var lastStr = await settings.GetAsync(lastPollKey, ct);

        // First run (or unparseable): anchor now so the first poll lands at the next scheduled time.
        if (lastStr is null || !DateTime.TryParse(lastStr, null, DateTimeStyles.RoundtripKind, out var lastPollUtc))
        {
            await settings.SetAsync(lastPollKey, nowUtc.ToString("o"), ct);
            return;
        }

        var lastLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(lastPollUtc, DateTimeKind.Utc), tz);
        if (schedule.GetNextOccurrence(lastLocal) > nowLocal) return; // not due yet

        await poll(ct);
        await settings.SetAsync(lastPollKey, nowUtc.ToString("o"), ct);
    }
}
