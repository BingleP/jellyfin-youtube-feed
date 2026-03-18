using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.YouTubeFeed;

public class PluginConfiguration : BasePluginConfiguration
{
    public PluginConfiguration()
    {
        CookiesFilePath = string.Empty;
        YtDlpPath = "/usr/bin/yt-dlp";
        FeedRefreshIntervalHours = 6;
        StrmFolderPath = string.Empty;
    }

    /// <summary>Full path to your YouTube cookies.txt file.</summary>
    public string CookiesFilePath { get; set; }

    /// <summary>Full path to the yt-dlp binary.</summary>
    public string YtDlpPath { get; set; }

    /// <summary>How often (in hours) to refresh the feed.</summary>
    public int FeedRefreshIntervalHours { get; set; }

    /// <summary>
    /// Folder where .strm files are written. Add this folder as a
    /// Jellyfin library (type: Movies or Videos) to browse the feed.
    /// </summary>
    public string StrmFolderPath { get; set; }
}
