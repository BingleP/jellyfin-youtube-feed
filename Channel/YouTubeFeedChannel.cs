using System.Text.RegularExpressions;
using Jellyfin.Plugin.YouTubeFeed.Api;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.YouTubeFeed.Channel;

public class YouTubeFeedChannel : IChannel, ISupportsLatestMedia
{
    private readonly FeedSync _feedSync;
    private readonly ILogger<YouTubeFeedChannel> _logger;

    private static readonly Regex YoutubeIdRegex = new(
        @"(?:youtube\.com/watch\?(?:[^&]*&)*v=|youtu\.be/)([A-Za-z0-9_-]{11})",
        RegexOptions.Compiled);

    public YouTubeFeedChannel(FeedSync feedSync, ILogger<YouTubeFeedChannel> logger)
    {
        _feedSync = feedSync;
        _logger = logger;
    }

    // ── IChannel ─────────────────────────────────────────────────────────────

    public string Name => "Invidious";
    public string Description => "Browse YouTube videos from your recommended feed";
    public string DataVersion => "4";
    public string HomePageUrl => "https://www.youtube.com/feed/recommended";
    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    public InternalChannelFeatures GetChannelFeatures() => new()
    {
        ContentTypes = [ChannelMediaContentType.Clip],
        MediaTypes = [ChannelMediaType.Video],
        SupportsContentDownloading = false,
        SupportsSortOrderToggle = false,
    };

    public bool IsEnabledFor(string userId) => true;

    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        => Task.FromResult(new DynamicImageResponse { HasImage = false });

    public IEnumerable<ImageType> GetSupportedChannelImages() => [];

    public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken ct)
    {
        await _feedSync.SyncIfDueAsync(StrmDirectory, ct).ConfigureAwait(false);
        var items = ScanStrmFiles().ToList();
        return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
    }

    // ── ISupportsLatestMedia ─────────────────────────────────────────────────

    public Task<IEnumerable<ChannelItemInfo>> GetLatestMedia(ChannelLatestMediaSearch request, CancellationToken ct)
        => Task.FromResult(ScanStrmFiles().Take(12));

    // ── STRM scanning ────────────────────────────────────────────────────────

    private string StrmDirectory
    {
        get
        {
            var dir = Path.Combine(Plugin.Instance!.DataFolderPath, "strm");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private IEnumerable<ChannelItemInfo> ScanStrmFiles()
    {
        foreach (var file in Directory.EnumerateFiles(StrmDirectory, "*.strm", SearchOption.AllDirectories))
        {
            string url;
            try
            {
                url = File.ReadAllText(file).Trim();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read {File}", file);
                continue;
            }

            var videoId = ExtractVideoId(url);
            if (videoId is null)
            {
                _logger.LogWarning("Could not extract YouTube video ID from {File}: {Url}", file, url);
                continue;
            }

            var title = Path.GetFileNameWithoutExtension(file);
            yield return StrmToItem(title, videoId);
        }
    }

    private static string? ExtractVideoId(string url)
    {
        var match = YoutubeIdRegex.Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    private ChannelItemInfo StrmToItem(string title, string videoId) => new()
    {
        Id = VideoIdToGuid(videoId),
        Name = title,
        Type = ChannelItemType.Media,
        MediaType = ChannelMediaType.Video,
        ContentType = ChannelMediaContentType.Clip,
        ImageUrl = $"https://i.ytimg.com/vi/{videoId}/maxresdefault.jpg",
        MediaSources = BuildMediaSources(videoId, title),
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string VideoIdToGuid(string videoId)
    {
        var hash = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes("invidious:" + videoId));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash).ToString();
    }

    private static List<MediaSourceInfo> BuildMediaSources(string videoId, string title) =>
    [
        new MediaSourceInfo
        {
            Id = VideoIdToGuid(videoId),
            Name = title,
            Path = $"http://127.0.0.1:3003/stream/{videoId}",
            Protocol = MediaBrowser.Model.MediaInfo.MediaProtocol.Http,
            Type = MediaSourceType.Default,
            IsRemote = false,
            Container = "ts",
            IsInfiniteStream = false,
            RequiresOpening = false,
            RequiresClosing = false,
            SupportsProbing = false,
            SupportsTranscoding = true,
            SupportsDirectPlay = false,
            SupportsDirectStream = true,
        }
    ];
}
