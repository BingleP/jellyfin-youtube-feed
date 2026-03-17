using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.YouTubeFeed;

public class PluginConfiguration : BasePluginConfiguration
{
    public PluginConfiguration()
    {
        CookiesFilePath = string.Empty;
        YtDlpPath = "/usr/bin/yt-dlp";
        FeedRefreshIntervalHours = 6;
    }

    /// <summary>
    /// Full path to your YouTube cookies.txt file exported from your browser.
    /// Used by yt-dlp to fetch your personalised recommended feed.
    /// Export using a browser extension such as "Get cookies.txt LOCALLY".
    /// Example: /home/youruser/youtube-cookies.txt
    /// </summary>
    public string CookiesFilePath { get; set; }

    /// <summary>
    /// Full path to the yt-dlp binary.
    /// Default: /usr/bin/yt-dlp
    /// </summary>
    public string YtDlpPath { get; set; }

    /// <summary>
    /// How often (in hours) to refresh the recommended feed and rewrite .strm files.
    /// Default: 6
    /// </summary>
    public int FeedRefreshIntervalHours { get; set; }
}
