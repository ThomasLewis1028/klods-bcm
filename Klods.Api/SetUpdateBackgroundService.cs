using Klods;
using Klods.Services;

namespace Klods.Api;

/// <summary>
/// Cron-scheduled background poll that re-imports locally-held sets which changed upstream. Ticks every
/// minute and runs when the configured schedule (UTC by default) is due — but only while the admin
/// toggle is on. Shares its scheduling logic with <see cref="RssBackgroundService"/> via
/// <see cref="CronPollRunner"/>.
/// </summary>
public class SetUpdateBackgroundService(IServiceScopeFactory scopeFactory, ILogger<SetUpdateBackgroundService> logger)
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
            catch (Exception ex) { logger.LogError(ex, "Set-update scheduler tick failed"); }

            try { await Task.Delay(Tick, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();

        await CronPollRunner.RunIfDueAsync(
            settings, logger, "set-update",
            SetUpdateService.EnabledKey, SetUpdateService.CronKey,
            SetUpdateService.TimezoneKey, SetUpdateService.LastPollAtKey,
            SetUpdateService.DefaultCron,
            async token =>
            {
                var max = await settings.GetIntAsync(SetUpdateService.MaxReimportsKey, SetUpdateService.DefaultMaxReimports, token);
                var svc = scope.ServiceProvider.GetRequiredService<SetUpdateService>();
                await svc.PollAsync(max, token);
            },
            ct);
    }
}
