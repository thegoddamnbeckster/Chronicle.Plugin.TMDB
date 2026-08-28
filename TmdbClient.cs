using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace Chronicle.Plugin.TMDB;

/// <summary>
/// Thin wrapper around the TMDB v3 REST API.
/// All methods throw <see cref="HttpRequestException"/> on network or API errors.
/// </summary>
internal sealed class TmdbClient
{
    private static readonly ILogger _log = Log.ForContext<TmdbClient>();

    /// <summary>
    /// TMDB's real limit is a short rolling per-second window, not a daily quota — unlike
    /// SIMKL, there is no "give up until tomorrow" condition here, only "back off briefly and
    /// retry." Capped so a misbehaving response can't stall enrichment indefinitely; bounded
    /// to one retry since a second 429 in a row almost certainly means something more
    /// persistent than a momentary burst, and the caller (ultimately Chronicle's own 25s
    /// ProviderCallGuard) should get to decide what happens next rather than this client
    /// looping quietly.
    /// </summary>
    private static readonly TimeSpan MaxRetryAfterWait = TimeSpan.FromSeconds(30);
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

    /// <summary>
    /// Every poster, backdrop, and logo TMDB holds for a movie, not just the single poster_path/
    /// backdrop_path on the detail record. Deliberately sends no <c>language</c>, same rationale
    /// as <see cref="GetCollectionImagesAsync"/> -- that parameter filters the gallery down to a
    /// handful, and Chronicle ingests losslessly and lets the user choose. This is a separate
    /// request rather than <c>append_to_response=images</c> on <see cref="GetMovieAsync"/>, which
    /// would inherit that call's <c>language</c> filter.
    /// </summary>
    public Task<TmdbImageList> GetMovieImagesAsync(string tmdbId, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/movie/{tmdbId}/images?api_key={_apiKey}";
        return GetAsync<TmdbImageList>(url, ct);
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

    /// <summary>
    /// Every poster, backdrop, and logo TMDB holds for a TV show, not just the single
    /// poster_path/backdrop_path on the detail record. Same no-<c>language</c> rationale as
    /// <see cref="GetMovieImagesAsync"/> and <see cref="GetCollectionImagesAsync"/>.
    /// </summary>
    public Task<TmdbImageList> GetTvImagesAsync(string tmdbId, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/tv/{tmdbId}/images?api_key={_apiKey}";
        return GetAsync<TmdbImageList>(url, ct);
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
        var response = await SendWithRetryAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("TMDB returned null response.");
    }

    /// <summary>
    /// One bounded retry on 429, honoring Retry-After (capped) — see MaxRetryAfterWait's own
    /// doc for why this is a short backoff, not a SIMKL-style multi-hour cutoff. TMDB previously
    /// had no 429 handling of any kind here; a rate-limited response just threw immediately via
    /// EnsureSuccessStatusCode with no chance to recover from what's normally a momentary burst.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(string url, CancellationToken ct)
    {
        var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.TooManyRequests)
            return response;

        var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
        var wait = retryAfter > MaxRetryAfterWait ? MaxRetryAfterWait : retryAfter;
        _log.Warning("TMDB: rate-limited (429); waiting {Seconds}s before one retry", wait.TotalSeconds);

        response.Dispose();
        await Task.Delay(wait, ct).ConfigureAwait(false);
        return await _http.GetAsync(url, ct).ConfigureAwait(false);
    }
}
