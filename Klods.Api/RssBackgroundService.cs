using Klods;
using Klods.Services;

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

        await CronPollRunner.RunIfDueAsync(
            settings, logger, "RSS",
            RssUpdateService.EnabledKey, RssUpdateService.CronKey,
            RssUpdateService.TimezoneKey, RssUpdateService.LastPollAtKey,
            RssUpdateService.DefaultCron,
            async token =>
            {
                var max = await settings.GetIntAsync(RssUpdateService.MaxImportsKey, RssUpdateService.DefaultMaxImports, token);
                var rss = scope.ServiceProvider.GetRequiredService<RssUpdateService>();
                await rss.PollAsync(max, token);
            },
            ct);
    }
}
