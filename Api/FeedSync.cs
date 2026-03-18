using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.YouTubeFeed.Api;

public class FeedSync
{
    private readonly ILogger<FeedSync> _logger;

    private static readonly Regex InvalidFilenameChars = new(@"[/\\:*?""<>|]", RegexOptions.Compiled);

    public FeedSync(ILogger<FeedSync> logger)
    {
        _logger = logger;
    }

    public async Task SyncAsync(CancellationToken ct)
    {
        var config = Plugin.Instance!.Configuration;

        var strmDir = config.StrmFolderPath;
        if (string.IsNullOrWhiteSpace(strmDir))
        {
            _logger.LogWarning("FeedSync: StrmFolderPath is not configured — skipping sync");
            return;
        }

        if (string.IsNullOrWhiteSpace(config.CookiesFilePath))
        {
            _logger.LogWarning("FeedSync: CookiesFilePath is not set — skipping sync");
            return;
        }

        if (!File.Exists(config.CookiesFilePath))
        {
            _logger.LogWarning("FeedSync: cookies file not found at {Path} — skipping sync", config.CookiesFilePath);
            return;
        }

        Directory.CreateDirectory(strmDir);
        _logger.LogInformation("FeedSync: starting sync → {Dir}", strmDir);

        try
        {
            await RunSyncAsync(strmDir, config.CookiesFilePath, config.YtDlpPath, ct).ConfigureAwait(false);
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
            _logger.LogError("FeedSync: yt-dlp exited {Code}: {Stderr}", process.ExitCode, stderr);
            return;
        }

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            _logger.LogWarning("FeedSync: no entries returned — cookies may be expired");
            return;
        }

        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int written = 0;

        foreach (var line in lines)
        {
            var parts = line.Split('\t', 2);
            if (parts.Length != 2) continue;

            var videoId = parts[0].Trim();
            var title   = parts[1].Trim();
            if (string.IsNullOrWhiteSpace(videoId) || videoId == "NA") continue;

            var safeTitle = InvalidFilenameChars.Replace(title, "_").Trim();
            if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = videoId;

            var filePath = Path.Combine(strmDir, $"{safeTitle}.strm");
            wanted.Add(filePath);

            var content = $"http://127.0.0.1:3003/stream/{videoId}";
            if (!File.Exists(filePath) || (await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false)).Trim() != content)
            {
                await File.WriteAllTextAsync(filePath, content, ct).ConfigureAwait(false);
                written++;
            }
        }

        int deleted = 0;
        foreach (var existing in Directory.EnumerateFiles(strmDir, "*.strm"))
        {
            if (!wanted.Contains(existing))
            {
                File.Delete(existing);
                deleted++;
            }
        }

        _logger.LogInformation("FeedSync: wrote {W} .strm files, removed {D} stale", written, deleted);
    }
}
