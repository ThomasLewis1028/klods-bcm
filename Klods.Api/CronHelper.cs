using NCrontab;

namespace Klods.Api;

/// <summary>Timezone + cron schedule helpers shared by the RSS scheduler and the admin preview endpoint.</summary>
public static class CronHelper
{
    public static TimeZoneInfo ResolveTimeZone(string? tzId)
    {
        if (string.IsNullOrWhiteSpace(tzId)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(tzId); }
        catch { return TimeZoneInfo.Utc; }
    }

    public static bool IsValidTimeZone(string? tzId)
    {
        if (string.IsNullOrWhiteSpace(tzId)) return false;
        try { TimeZoneInfo.FindSystemTimeZoneById(tzId); return true; }
        catch { return false; }
    }

    /// <summary>
    /// The next <paramref name="count"/> occurrences of <paramref name="cron"/> after now, expressed in
    /// <paramref name="tz"/> local time. Returns an empty list if the cron is invalid.
    /// </summary>
    public static List<DateTime> NextOccurrencesLocal(string cron, TimeZoneInfo tz, int count)
    {
        var schedule = CrontabSchedule.TryParse(cron);
        if (schedule is null) return [];

        var cursor = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var list = new List<DateTime>(count);
        for (var i = 0; i < count; i++)
        {
            cursor = schedule.GetNextOccurrence(cursor);
            list.Add(cursor);
        }
        return list;
    }
}
