using Chronicle.Plugins;
using Chronicle.Plugins.Models;

namespace Chronicle.Plugin.TMDB;

/// <summary>
/// Chronicle metadata provider for The Movie Database (TMDB).
/// Supports the "movie" and "tv" media types.
/// </summary>
public sealed class TmdbMetadataProvider : IMetadataProvider
{
    // ── IMetadataProvider identity ────────────────────────────────────────────

    public string PluginId => "tmdb";
    public string Name     => "TMDB";
    public string Version  => "1.0.0";
    public string Author   => "Chronicle Contributors";

    // ── Settings keys ─────────────────────────────────────────────────────────

    private const string KeyApiKey        = "api_key";
    private const string KeyLanguage      = "language";
    private const string KeyIncludeAdult  = "include_adult";
    private const string KeyPosterSize    = "poster_size";
    private const string KeyBackdropSize  = "backdrop_size";

    // ── Live configuration (populated by Configure()) ─────────────────────────

    private TmdbClient? _client;

    // ── IMetadataProvider: static declarations ────────────────────────────────

    public MediaTypeSupport[] GetSupportedMediaTypes() =>
    [
        new MediaTypeSupport
        {
            MediaTypeName   = "movie",
            DefaultPriority = 10,
            SupportedFields = ["title", "overview", "year", "poster_url", "backdrop_url",
                               "runtime_minutes", "genres", "cast", "directors", "rating"],
        },
        new MediaTypeSupport
        {
            MediaTypeName   = "tv",
            DefaultPriority = 10,
            SupportedFields = ["title", "overview", "year", "poster_url", "backdrop_url",
                               "runtime_minutes", "genres", "cast", "rating"],
        },
    ];

    public PluginSettingsSchema GetSettingsSchema() => new()
    {
        Settings =
        [
            new SettingDefinition
            {
                Key         = KeyApiKey,
                Label       = "TMDB API Key",
                Description = "Your v3 API key from https://www.themoviedb.org/settings/api",
                Type        = SettingType.Password,
                Required    = true,
            },
            new SettingDefinition
            {
                Key          = KeyLanguage,
                Label        = "Language",
                Description  = "BCP 47 language tag used for titles and overviews (e.g. en-US, de-DE).",
                Type         = SettingType.Text,
                Required     = false,
                DefaultValue = "en-US",
            },
            new SettingDefinition
            {
                Key          = KeyIncludeAdult,
                Label        = "Include Adult Content",
                Description  = "Whether to include adult titles in search results.",
                Type         = SettingType.Boolean,
                Required     = false,
                DefaultValue = "false",
            },
            new SettingDefinition
            {
                Key          = KeyPosterSize,
                Label        = "Poster Image Size",
                Description  = "TMDB image size for posters. Larger sizes use more bandwidth.",
                Type         = SettingType.Dropdown,
                Required     = false,
                DefaultValue = "w500",
                Options      = [
                    new SelectOption { Value = "w185",  Label = "w185 — Small"  },
                    new SelectOption { Value = "w342",  Label = "w342 — Medium" },
                    new SelectOption { Value = "w500",  Label = "w500 — Large (default)" },
                    new SelectOption { Value = "w780",  Label = "w780 — XL"    },
                    new SelectOption { Value = "original", Label = "original — Full resolution" },
                ],
            },
            new SettingDefinition
            {
                Key          = KeyBackdropSize,
                Label        = "Backdrop Image Size",
                Description  = "TMDB image size for backdrop/banner images.",
                Type         = SettingType.Dropdown,
                Required     = false,
                DefaultValue = "w1280",
                Options      = [
                    new SelectOption { Value = "w300",  Label = "w300 — Small"  },
                    new SelectOption { Value = "w780",  Label = "w780 — Medium" },
                    new SelectOption { Value = "w1280", Label = "w1280 — Large (default)" },
                    new SelectOption { Value = "original", Label = "original — Full resolution" },
                ],
            },
        ],
    };

    // ── IMetadataProvider: configuration ─────────────────────────────────────

    public void Configure(IReadOnlyDictionary<string, string> settings)
    {
        settings.TryGetValue(KeyApiKey,       out var apiKey);
        settings.TryGetValue(KeyLanguage,     out var language);
        settings.TryGetValue(KeyIncludeAdult, out var includeAdultStr);
        settings.TryGetValue(KeyPosterSize,   out var posterSize);
        settings.TryGetValue(KeyBackdropSize, out var backdropSize);

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("TMDB plugin requires 'api_key' to be configured.");

        var http = new HttpClient();
        _client = new TmdbClient(
            http,
            apiKey,
            language   ?? "en-US",
            bool.TryParse(includeAdultStr, out var ia) && ia
        );
        _posterSize   = posterSize   ?? "w500";
        _backdropSize = backdropSize ?? "w1280";
    }

    private string _posterSize   = "w500";
    private string _backdropSize = "w1280";

    // ── IMetadataProvider: search ─────────────────────────────────────────────

    public async Task<MediaMetadata> SearchAsync(string query, string mediaType,
        CancellationToken ct = default)
    {
        EnsureConfigured();

        return mediaType.ToLowerInvariant() switch
        {
            "tv" => await SearchTvAsync(query, ct).ConfigureAwait(false),
            _    => await SearchMovieAsync(query, ct).ConfigureAwait(false),   // default → movie
        };
    }

    private async Task<MediaMetadata> SearchMovieAsync(string query, CancellationToken ct)
    {
        var resp = await _client!.SearchMoviesAsync(query, ct).ConfigureAwait(false);
        return new MediaMetadata
        {
            Source       = "tmdb",
            TotalResults = resp.TotalResults,
            Results      = resp.Results.Select(MapMovie).ToList(),
        };
    }

    private async Task<MediaMetadata> SearchTvAsync(string query, CancellationToken ct)
    {
        var resp = await _client!.SearchTvAsync(query, ct).ConfigureAwait(false);
        return new MediaMetadata
        {
            Source       = "tmdb",
            TotalResults = resp.TotalResults,
            Results      = resp.Results.Select(MapTv).ToList(),
        };
    }

    // ── IMetadataProvider: get by ID ──────────────────────────────────────────

    /// <summary>
    /// Fetches full details for a specific item.
    /// The external ID format is "{type}:{tmdbId}", e.g. "movie:550" or "tv:1399".
    /// </summary>
    public async Task<MediaMetadata> GetByIdAsync(string externalId, CancellationToken ct = default)
    {
        EnsureConfigured();

        var parts = externalId.Split(':', 2);
        if (parts.Length != 2)
            throw new ArgumentException($"Invalid TMDB external ID format: '{externalId}'. Expected 'type:id'.");

        var (type, id) = (parts[0].ToLowerInvariant(), parts[1]);

        return type switch
        {
            "tv"    => MapTv(await _client!.GetTvAsync(id, ct).ConfigureAwait(false)),
            _       => MapMovie(await _client!.GetMovieAsync(id, ct).ConfigureAwait(false)),
        };
    }

    // ── IMetadataProvider: image ──────────────────────────────────────────────

    public Task<byte[]> GetImageAsync(string url, CancellationToken ct = default)
    {
        EnsureConfigured();
        return _client!.GetImageAsync(url, ct);
    }

    // ── IMetadataProvider: health ─────────────────────────────────────────────

    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        if (_client is null) return false;
        return await _client.PingAsync(ct).ConfigureAwait(false);
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private MediaMetadata MapMovie(TmdbMovie m) => new()
    {
        ExternalId      = $"movie:{m.Id}",
        Source          = "tmdb",
        Title           = m.Title,
        Overview        = m.Overview,
        Year            = ParseYear(m.ReleaseDate),
        PosterUrl       = m.PosterPath   is not null ? _client!.BuildImageUrl(m.PosterPath,   _posterSize)   : null,
        BackdropUrl     = m.BackdropPath is not null ? _client!.BuildImageUrl(m.BackdropPath, _backdropSize) : null,
        RuntimeMinutes  = m.Runtime,
        Rating          = m.VoteAverage,
        Genres          = m.Genres?.Select(g => g.Name).ToList() ?? [],
        Cast            = m.Credits?.Cast?.OrderBy(c => c.Order).Select(c => c.Name).Take(10).ToList() ?? [],
        Directors       = m.Credits?.Crew?.Where(c => c.Job == "Director").Select(c => c.Name).ToList() ?? [],
    };

    private MediaMetadata MapTv(TmdbTv t) => new()
    {
        ExternalId      = $"tv:{t.Id}",
        Source          = "tmdb",
        Title           = t.Name,
        Overview        = t.Overview,
        Year            = ParseYear(t.FirstAirDate),
        PosterUrl       = t.PosterPath   is not null ? _client!.BuildImageUrl(t.PosterPath,   _posterSize)   : null,
        BackdropUrl     = t.BackdropPath is not null ? _client!.BuildImageUrl(t.BackdropPath, _backdropSize) : null,
        RuntimeMinutes  = t.EpisodeRunTime?.FirstOrDefault(),
        Rating          = t.VoteAverage,
        Genres          = t.Genres?.Select(g => g.Name).ToList() ?? [],
        Cast            = t.Credits?.Cast?.OrderBy(c => c.Order).Select(c => c.Name).Take(10).ToList() ?? [],
        Directors       = [],   // TV uses Crew differently; simplified for now
    };

    private static int? ParseYear(string? date) =>
        date is { Length: >= 4 } && int.TryParse(date[..4], out var y) ? y : null;

    private void EnsureConfigured()
    {
        if (_client is null)
            throw new InvalidOperationException("TmdbMetadataProvider has not been configured. Call Configure() first.");
    }
}
