using Jellyfin.Plugin.InvidiousChannel.Api;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.InvidiousChannel.Channel;

public class InvidiousChannel : IChannel, ISupportsLatestMedia
{
    private readonly InvidiousApiClient _api;
    private readonly ILogger<InvidiousChannel> _logger;

    private const string FolderTrending = "trending";
    private const string FolderPopular = "popular";

    public InvidiousChannel(InvidiousApiClient api, ILogger<InvidiousChannel> logger)
    {
        _api = api;
        _logger = logger;
    }

    // ── IChannel ─────────────────────────────────────────────────────────────

    public string Name => "Invidious";
    public string Description => "Browse YouTube through your local Invidious instance";
    public string DataVersion => "3";
    public string HomePageUrl => Plugin.Instance?.Configuration.InvidiousUrl ?? "http://invidious.lan";
    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    public InternalChannelFeatures GetChannelFeatures() => new()
    {
        ContentTypes = [ChannelMediaContentType.Clip],
        MediaTypes = [ChannelMediaType.Video],
        SupportsContentDownloading = false,
        SupportsSortOrderToggle = false,
    };

    public bool IsEnabledFor(string userId) => true;

    // Jellyfin calls this to display a channel icon in the sidebar.
    // Return null to use the default icon.
    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        => Task.FromResult(new DynamicImageResponse { HasImage = false });

    public IEnumerable<ImageType> GetSupportedChannelImages()
        => [];

    public Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken ct)
    {
        return query.FolderId switch
        {
            null or "" => Task.FromResult(GetRootFolders()),
            FolderTrending => GetVideoItems(() => _api.GetTrendingAsync(ct)),
            FolderPopular => GetVideoItems(() => _api.GetPopularAsync(ct)),
            _ => Task.FromResult(new ChannelItemResult()),
        };
    }

    // ── ISupportsLatestMedia ─────────────────────────────────────────────────

    public async Task<IEnumerable<ChannelItemInfo>> GetLatestMedia(ChannelLatestMediaSearch request, CancellationToken ct)
    {
        var videos = await _api.GetTrendingAsync(ct).ConfigureAwait(false);
        return videos.Take(12).Select(VideoToItem);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ChannelItemResult GetRootFolders() => new()
    {
        Items =
        [
            new ChannelItemInfo { Id = FolderTrending, Name = "Trending", Type = ChannelItemType.Folder },
            new ChannelItemInfo { Id = FolderPopular,  Name = "Popular",  Type = ChannelItemType.Folder },
        ],
        TotalRecordCount = 2,
    };

    private async Task<ChannelItemResult> GetVideoItems(Func<Task<List<InvidiousVideo>>> fetch)
    {
        var videos = await fetch().ConfigureAwait(false);
        var items = videos.Select(VideoToItem).ToList();
        return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
    }

    private ChannelItemInfo VideoToItem(InvidiousVideo v) => new()
    {
        Id = v.VideoId,
        Name = v.Title,
        Overview = v.Description,
        Type = ChannelItemType.Media,
        MediaType = ChannelMediaType.Video,
        ContentType = ChannelMediaContentType.Clip,
        ImageUrl = BestThumbnail(v.VideoThumbnails),
        RunTimeTicks = TimeSpan.FromSeconds(v.LengthSeconds).Ticks,
        DateCreated = DateTimeOffset.FromUnixTimeSeconds(v.Published).UtcDateTime,
        Tags = [v.Author],
        MediaSources = BuildMediaSources(v.VideoId, v.Title),
    };

    /// <summary>Derive a deterministic GUID from a YouTube video ID.</summary>
    private static string VideoIdToGuid(string videoId)
    {
        var hash = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes("invidious:" + videoId));
        // Set UUID v3 version and variant bits
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash).ToString();
    }

    private List<MediaSourceInfo> BuildMediaSources(string videoId, string title) =>
    [
        new MediaSourceInfo
        {
            Id = VideoIdToGuid(videoId),
            Name = title,
            // Routes: Jellyfin → Invidious proxy (invidious.lan) → Companion → Mullvad → YouTube CDN
            // itag 22 = 720p mp4 (H.264+AAC, progressive, no DASH needed)
            Path = _api.GetStreamUrl(videoId),
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

    private static string? BestThumbnail(List<InvidiousThumbnail> thumbs)
    {
        foreach (var quality in new[] { "maxresdefault", "sddefault", "high", "medium", "default" })
        {
            var t = thumbs.FirstOrDefault(x => x.Quality == quality);
            if (t is not null) return t.Url;
        }
        return thumbs.FirstOrDefault()?.Url;
    }
}
