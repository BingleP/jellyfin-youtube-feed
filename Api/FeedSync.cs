using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.YouTubeFeed.Api;

public class FeedSync
{
    private readonly ILogger<FeedSync> _logger;
    private DateTime _lastSync = DateTime.MinValue;

    // Characters not allowed in filenames on Linux/Windows
    private static readonly Regex InvalidFilenameChars = new(@"[/\\:*?""<>|]", RegexOptions.Compiled);

    public FeedSync(ILogger<FeedSync> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Runs a feed sync if the configured interval has elapsed since the last sync.
    /// </summary>
    public async Task SyncIfDueAsync(string strmDirectory, CancellationToken ct)
    {
        var config = Plugin.Instance!.Configuration;

        if (DateTime.UtcNow - _lastSync < TimeSpan.FromHours(config.FeedRefreshIntervalHours))
            return;

        if (string.IsNullOrWhiteSpace(config.CookiesFilePath))
        {
            _logger.LogWarning("FeedSync: CookiesFilePath is not set in plugin settings — skipping sync");
            return;
        }

        if (!File.Exists(config.CookiesFilePath))
        {
            _logger.LogWarning("FeedSync: cookies file not found at {Path} — skipping sync", config.CookiesFilePath);
            return;
        }

        _lastSync = DateTime.UtcNow;
        _logger.LogInformation("FeedSync: starting recommended feed sync");

        try
        {
            await RunSyncAsync(strmDirectory, config.CookiesFilePath, config.YtDlpPath, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FeedSync: sync failed");
        }
    }

    private async Task RunSyncAsync(string strmDir, string cookiesPath, string ytDlpPath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            // --flat-playlist: don't download, just list entries
            // --print: output videoId<tab>title for each entry
            ArgumentList =
            {
                "--cookies", cookiesPath,
                "--flat-playlist",
                "--no-warnings",
                "--print", "%(id)s\t%(title)s",
                "https://www.youtube.com/feed/recommended",
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            _logger.LogError("FeedSync: yt-dlp exited with code {Code}: {Stderr}", process.ExitCode, stderr);
            return;
        }

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            _logger.LogWarning("FeedSync: yt-dlp returned no entries — cookies may be expired");
            return;
        }

        // Clear existing strm files so stale recommendations are removed
        foreach (var old in Directory.EnumerateFiles(strmDir, "*.strm"))
            File.Delete(old);

        int written = 0;
        foreach (var line in lines)
        {
            var parts = line.Split('\t', 2);
            if (parts.Length != 2) continue;

            var videoId = parts[0].Trim();
            var title = parts[1].Trim();

            if (string.IsNullOrWhiteSpace(videoId) || videoId == "NA") continue;

            var safeTitle = InvalidFilenameChars.Replace(title, "_").Trim();
            if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = videoId;

            var filePath = Path.Combine(strmDir, $"{safeTitle}.strm");
            await File.WriteAllTextAsync(filePath, $"https://www.youtube.com/watch?v={videoId}", ct)
                .ConfigureAwait(false);
            written++;
        }

        _logger.LogInformation("FeedSync: wrote {Count} .strm files", written);
    }
}
