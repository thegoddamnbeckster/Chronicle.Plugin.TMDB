using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chronicle.Plugin.TMDB;

/// <summary>
/// Thin wrapper around the TMDB v3 REST API.
/// All methods throw <see cref="HttpRequestException"/> on network or API errors.
/// </summary>
internal sealed class TmdbClient
{
    private const string BaseUrl = "https://api.themoviedb.org/3";
    private const string ImageBase = "https://image.tmdb.org/t/p/";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _language;
    private readonly bool _includeAdult;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public TmdbClient(HttpClient http, string apiKey, string language, bool includeAdult)
    {
        _http = http;
        _apiKey = apiKey;
        _language = language;
        _includeAdult = includeAdult;
    }

    // ── Movies ────────────────────────────────────────────────────────────────

    public Task<TmdbSearchResponse<TmdbMovie>> SearchMoviesAsync(string query, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/search/movie?api_key={_apiKey}&language={_language}" +
                  $"&include_adult={_includeAdult.ToString().ToLower()}&query={Uri.EscapeDataString(query)}";
        return GetAsync<TmdbSearchResponse<TmdbMovie>>(url, ct);
    }

    public Task<TmdbMovie> GetMovieAsync(string tmdbId, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/movie/{tmdbId}?api_key={_apiKey}&language={_language}&append_to_response=credits";
        return GetAsync<TmdbMovie>(url, ct);
    }

    // ── TV Shows ─────────────────────────────────────────────────────────────

    public Task<TmdbSearchResponse<TmdbTv>> SearchTvAsync(string query, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/search/tv?api_key={_apiKey}&language={_language}&query={Uri.EscapeDataString(query)}";
        return GetAsync<TmdbSearchResponse<TmdbTv>>(url, ct);
    }

    public Task<TmdbTv> GetTvAsync(string tmdbId, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/tv/{tmdbId}?api_key={_apiKey}&language={_language}&append_to_response=credits";
        return GetAsync<TmdbTv>(url, ct);
    }

    // ── Images ────────────────────────────────────────────────────────────────

    /// <summary>Downloads raw image bytes from the TMDB image CDN.</summary>
    public async Task<byte[]> GetImageAsync(string url, CancellationToken ct = default)
    {
        var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Builds a full TMDB image URL from a poster/backdrop path.</summary>
    public string BuildImageUrl(string path, string size = "w500") =>
        $"{ImageBase}{size}{path}";

    // ── Health ────────────────────────────────────────────────────────────────

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"{BaseUrl}/configuration?api_key={_apiKey}";
            var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("TMDB returned null response.");
    }
}
