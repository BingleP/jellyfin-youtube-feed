using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.YouTubeFeed.Api;

/// <summary>
/// Background service that runs FeedSync on startup and then every
/// FeedRefreshIntervalHours hours. No channel interface required.
/// </summary>
public class FeedSyncService : IHostedService, IDisposable
{
    private readonly FeedSync _feedSync;
    private readonly ILogger<FeedSyncService> _logger;
    private Timer? _timer;

    public FeedSyncService(FeedSync feedSync, ILogger<FeedSyncService> logger)
    {
        _feedSync = feedSync;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        var intervalHours = Plugin.Instance?.Configuration.FeedRefreshIntervalHours ?? 6;
        _logger.LogInformation("FeedSyncService: starting, interval = {H}h", intervalHours);

        // Fire immediately, then repeat on the configured interval
        _timer = new Timer(
            async _ => await RunSync(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromHours(intervalHours));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    private async Task RunSync()
    {
        try
        {
            await _feedSync.SyncAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FeedSyncService: unhandled error during sync");
        }
    }
}
