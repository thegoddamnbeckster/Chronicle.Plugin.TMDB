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

    public Task<TmdbSearchResponse<TmdbMovie>> SearchMoviesAsync(string query, int? year = null, CancellationToken ct = default)
    {
        var yearParam = year.HasValue ? $"&primary_release_year={year}" : string.Empty;
        var url = $"{BaseUrl}/search/movie?api_key={_apiKey}&language={_language}" +
                  $"&include_adult={_includeAdult.ToString().ToLower()}&query={Uri.EscapeDataString(query)}{yearParam}";
        return GetAsync<TmdbSearchResponse<TmdbMovie>>(url, ct);
    }

    public Task<TmdbMovie> GetMovieAsync(string tmdbId, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/movie/{tmdbId}?api_key={_apiKey}&language={_language}&append_to_response=credits,release_dates,videos";
        return GetAsync<TmdbMovie>(url, ct);
    }

    public Task<TmdbCollection> GetCollectionAsync(int collectionId, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/collection/{collectionId}?api_key={_apiKey}&language={_language}";
        return GetAsync<TmdbCollection>(url, ct);
    }

    /// <summary>
    /// Every poster and backdrop TMDB holds for a collection, not just the one on the detail
    /// record. Deliberately sends no <c>language</c> — that parameter filters images down to a
    /// handful, and Chronicle ingests losslessly and lets the user choose. This is why it's a
    /// separate request rather than <c>append_to_response=images</c>, which inherits the
    /// language filter from the detail call.
    /// </summary>
    public Task<TmdbImageList> GetCollectionImagesAsync(int collectionId, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/collection/{collectionId}/images?api_key={_apiKey}";
        return GetAsync<TmdbImageList>(url, ct);
    }

    // ── TV Shows ─────────────────────────────────────────────────────────────

    public Task<TmdbSearchResponse<TmdbTv>> SearchTvAsync(string query, int? year = null, CancellationToken ct = default)
    {
        var yearParam = year.HasValue ? $"&first_air_date_year={year}" : string.Empty;
        var url = $"{BaseUrl}/search/tv?api_key={_apiKey}&language={_language}&query={Uri.EscapeDataString(query)}{yearParam}";
        return GetAsync<TmdbSearchResponse<TmdbTv>>(url, ct);
    }

    public Task<TmdbTv> GetTvAsync(string tmdbId, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/tv/{tmdbId}?api_key={_apiKey}&language={_language}&append_to_response=credits,content_ratings,videos,external_ids";
        return GetAsync<TmdbTv>(url, ct);
    }

    public Task<TmdbSeason> GetTvSeasonAsync(string showId, string seasonNumber, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/tv/{showId}/season/{seasonNumber}?api_key={_apiKey}&language={_language}";
        return GetAsync<TmdbSeason>(url, ct);
    }

    public Task<TmdbEpisode> GetTvEpisodeAsync(string showId, string seasonNumber, string episodeNumber, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/tv/{showId}/season/{seasonNumber}/episode/{episodeNumber}?api_key={_apiKey}&language={_language}";
        return GetAsync<TmdbEpisode>(url, ct);
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
