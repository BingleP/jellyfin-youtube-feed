using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.InvidiousChannel.Api;

public class InvidiousVideo
{
    [JsonPropertyName("videoId")]
    public string VideoId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("authorId")]
    public string AuthorId { get; set; } = string.Empty;

    [JsonPropertyName("lengthSeconds")]
    public int LengthSeconds { get; set; }

    [JsonPropertyName("viewCount")]
    public long ViewCount { get; set; }

    [JsonPropertyName("published")]
    public long Published { get; set; }

    [JsonPropertyName("videoThumbnails")]
    public List<InvidiousThumbnail> VideoThumbnails { get; set; } = new();

    [JsonPropertyName("formatStreams")]
    public List<InvidiousFormatStream> FormatStreams { get; set; } = new();
}

public class InvidiousThumbnail
{
    [JsonPropertyName("quality")]
    public string Quality { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}

public class InvidiousFormatStream
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("itag")]
    public string Itag { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("quality")]
    public string Quality { get; set; } = string.Empty;

    [JsonPropertyName("container")]
    public string Container { get; set; } = string.Empty;

    [JsonPropertyName("resolution")]
    public string Resolution { get; set; } = string.Empty;
}

public class InvidiousSearchResult
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("videoId")]
    public string VideoId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("authorId")]
    public string AuthorId { get; set; } = string.Empty;

    [JsonPropertyName("lengthSeconds")]
    public int LengthSeconds { get; set; }

    [JsonPropertyName("viewCount")]
    public long ViewCount { get; set; }

    [JsonPropertyName("published")]
    public long Published { get; set; }

    [JsonPropertyName("videoThumbnails")]
    public List<InvidiousThumbnail> VideoThumbnails { get; set; } = new();

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}
