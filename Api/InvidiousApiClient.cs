using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.InvidiousChannel.Api;

public class InvidiousApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<InvidiousApiClient> _logger;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public InvidiousApiClient(IHttpClientFactory httpClientFactory, ILogger<InvidiousApiClient> logger)
    {
        _http = httpClientFactory.CreateClient(nameof(InvidiousApiClient));
        _logger = logger;
    }

    private string Base => Plugin.Instance?.Configuration.InvidiousUrl.TrimEnd('/')
        ?? "http://invidious.lan";

    private string Api(string path) => $"{Base}/api/v1{path}";

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<List<InvidiousVideo>> GetTrendingAsync(CancellationToken ct)
        => await FetchList<InvidiousVideo>(Api("/trending"), ct);

    public async Task<List<InvidiousVideo>> GetPopularAsync(CancellationToken ct)
        => await FetchList<InvidiousVideo>(Api("/popular"), ct);

    public async Task<List<InvidiousSearchResult>> SearchAsync(string query, int page, CancellationToken ct)
    {
        var url = Api($"/search?q={Uri.EscapeDataString(query)}&type=video&page={page}");
        return await FetchList<InvidiousSearchResult>(url, ct);
    }

    public async Task<InvidiousVideo?> GetVideoAsync(string videoId, CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<InvidiousVideo>(Api($"/videos/{videoId}"), _json, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch video {VideoId}", videoId);
            return null;
        }
    }

    /// <summary>
    /// Returns a stream URL pointing to the local ytstream-proxy service.
    /// The proxy fetches fresh adaptive video+audio URLs from Invidious and combines
    /// them via ffmpeg on the fly. For videos with progressive formats, it redirects
    /// directly to the Invidious /latest_version endpoint instead.
    /// </summary>
    public string GetStreamUrl(string videoId)
        => $"http://127.0.0.1:3003/stream/{videoId}";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<List<T>> FetchList<T>(string url, CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Invidious fetch: {Url}", url);
            var result = await _http.GetFromJsonAsync<List<T>>(url, _json, ct).ConfigureAwait(false);
            return result ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invidious request failed: {Url}", url);
            return new();
        }
    }
}
