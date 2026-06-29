using Klods;
using Klods.Services;

namespace Klods.Api;

/// <summary>Hourly background poll of the Rebrickable RSS feed — runs only while the admin toggle is on.</summary>
public class RssBackgroundService(IServiceScopeFactory scopeFactory, ILogger<RssBackgroundService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let startup + migrations settle before the first poll.
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
                if (await settings.GetBoolAsync(RssUpdateService.EnabledKey, ct: stoppingToken))
                {
                    var rss = scope.ServiceProvider.GetRequiredService<RssUpdateService>();
                    await rss.PollAsync(ct: stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RSS background poll failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
