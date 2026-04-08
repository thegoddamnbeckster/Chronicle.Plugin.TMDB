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

    /// <summary>Test-only constructor that injects a pre-built client.</summary>
    internal TmdbMetadataProvider(TmdbClient client,
        string posterSize = "w500", string backdropSize = "w1280")
    {
        _client       = client;
        _posterSize   = posterSize;
        _backdropSize = backdropSize;
    }

    /// <summary>Required for public instantiation by the host (no-arg).</summary>
    public TmdbMetadataProvider() { }

    // ── IMetadataProvider: static declarations ────────────────────────────────

    public MediaTypeSupport[] GetSupportedMediaTypes() =>
    [
        // Chronicle's DB uses "movies" (plural); "movie" is kept for any legacy records.
        // Both map to the same TMDB /search/movie and /movie/{id} endpoints.
        new MediaTypeSupport
        {
            MediaTypeName   = "movies",
            DefaultPriority = 10,
            SupportedFields = ["title", "overview", "year", "poster_url", "backdrop_url",
                               "runtime_minutes", "genres", "cast", "directors", "rating"],
        },
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

    // Matches a trailing " (YYYY)" or "(YYYY)" year suffix — common in file-scanner folder names.
    private static readonly System.Text.RegularExpressions.Regex YearSuffixRe =
        new(@"\s*\((\d{4})\)\s*$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Minimum score for a Stage 1a result to short-circuit Stage 1b (year-less search).</summary>
    private const int ExactMatchThreshold = 60;

    public async Task<IReadOnlyList<ScoredCandidate>> SearchAsync(
        MediaSearchContext context, CancellationToken ct = default)
    {
        EnsureConfigured();

        // Build the ordered list of titles to try.  AltTitles already contains the
        // year-stripped name, filename stem, and qualifier-stripped forms in order.
        // Fall back to [context.Name] when none were provided.
        // Deduplicate to avoid firing the same TMDB query twice (e.g. when Name and
        // AltTitles[0] are identical after the enrichment service builds alt-title variants).
        var titlesToTry = (context.AltTitles is { Count: > 0 }
            ? context.AltTitles
            : (IEnumerable<string>)[context.Name])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Stage 1a — each AltTitle WITH year (allows early exit at ExactMatchThreshold).
        var stage1aCandidates = new List<ScoredCandidate>();
        bool foundHighScore = false;

        foreach (var rawTitle in titlesToTry)
        {
            // Strip any residual "(YYYY)" suffix — AltTitles builder already does this,
            // but apply YearSuffixRe as a safety net for the Name fallback path.
            int? year = context.Year;
            string title = rawTitle;
            var yearMatch = YearSuffixRe.Match(title);
            if (yearMatch.Success)
            {
                year ??= int.Parse(yearMatch.Groups[1].Value);
                title = title[..yearMatch.Index].Trim();
            }

            var movieResp = await _client!.SearchMoviesAsync(title, year, ct).ConfigureAwait(false);
            foreach (var m in movieResp.Results ?? [])
                stage1aCandidates.Add(ScoreCandidate(context, MapMovie(m)));

            var tvResp = await _client!.SearchTvAsync(title, year, ct).ConfigureAwait(false);
            foreach (var t in tvResp.Results ?? [])
                stage1aCandidates.Add(ScoreCandidate(context, MapTv(t)));

            // Short-circuit if any candidate already has an exact title match.
            if (stage1aCandidates.Any(c => c.Score >= ExactMatchThreshold))
            {
                foundHighScore = true;
                break;
            }
        }

        // If Stage 1a produced a strong hit, return immediately without year-less fallback.
        if (foundHighScore)
        {
            return stage1aCandidates
                .Where(c => c.Metadata.ExternalId is not null)
                .OrderByDescending(c => c.Score)
                .ThenByDescending(c => GetPopularity(c.Metadata))
                .Take(10)
                .ToList();
        }

        // Stage 1b — each AltTitle WITHOUT year.
        var stage1bCandidates = new List<ScoredCandidate>();

        foreach (var rawTitle in titlesToTry)
        {
            string title = rawTitle;
            var yearMatch = YearSuffixRe.Match(title);
            if (yearMatch.Success)
                title = title[..yearMatch.Index].Trim();

            var movieResp = await _client!.SearchMoviesAsync(title, year: null, ct).ConfigureAwait(false);
            foreach (var m in movieResp.Results ?? [])
                stage1bCandidates.Add(ScoreCandidate(context, MapMovie(m)));

            var tvResp = await _client!.SearchTvAsync(title, year: null, ct).ConfigureAwait(false);
            foreach (var t in tvResp.Results ?? [])
                stage1bCandidates.Add(ScoreCandidate(context, MapTv(t)));
        }

        // Merge stage 1a (year-confirmed) first, then stage 1b.
        var allCandidates = stage1aCandidates.Concat(stage1bCandidates);

        return allCandidates
            .Where(c => c.Metadata.ExternalId is not null)
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => GetPopularity(c.Metadata))
            .Take(10)
            .ToList();
    }

    /// <summary>Reads the popularity value stored in a candidate's ExtendedData.</summary>
    private static double GetPopularity(MediaMetadata m)
    {
        if (m.ExtendedData is not { } ext)
            return 0d;

        if (ext.TryGetProperty("popularity", out var prop)
            && prop.ValueKind == System.Text.Json.JsonValueKind.Number)
            return prop.GetDouble();

        return 0d;
    }

    private static ScoredCandidate ScoreCandidate(MediaSearchContext ctx, MediaMetadata candidate)
    {
        int score = 0;
        var reasons = new List<string>();

        var cn = Normalize(candidate.Title ?? string.Empty);

        // Build the best normalized query name to compare against.
        // AltTitles[0] is typically the year-stripped PreciseName or clean name; prefer it
        // over ctx.Name which may still carry a "(YYYY)" suffix from the fallback path.
        // If no AltTitles, strip any year suffix from ctx.Name manually.
        string rawQueryName = ctx.AltTitles is { Count: > 0 }
            ? ctx.AltTitles[0]
            : ctx.Name;
        var suffixMatch = YearSuffixRe.Match(rawQueryName);
        if (suffixMatch.Success)
            rawQueryName = rawQueryName[..suffixMatch.Index].Trim();
        var qn = Normalize(rawQueryName);

        if (string.Equals(cn, qn, StringComparison.Ordinal))
        {
            score += 60;
            reasons.Add("title exact");
        }
        else if (cn.Contains(qn, StringComparison.Ordinal) || qn.Contains(cn, StringComparison.Ordinal))
        {
            score += 30;
            reasons.Add("title contains");
        }

        if (ctx.Year.HasValue && candidate.Year.HasValue)
        {
            if (ctx.Year.Value == candidate.Year.Value)
            {
                score += 20;
                reasons.Add("year exact");
            }
            else if (Math.Abs(ctx.Year.Value - candidate.Year.Value) == 1)
            {
                score += 10;
                reasons.Add("year ±1");
            }
            else
            {
                score -= 10;
                reasons.Add("year mismatch");
            }
        }

        // Precise-name tiebreaker: use the exact title from file metadata (NFO <title>) when
        // available.  Unlike the normalised comparison above, this keeps punctuation so that
        // "What If...?" stays distinct from "What If".  Only applied when PreciseName is
        // explicitly set — never falls back to the folder/item name, which would favour the
        // wrong candidate (the exact-match show) over the right one (the show with "...?").
        if (!string.IsNullOrEmpty(ctx.PreciseName))
        {
            var pn = ctx.PreciseName.Trim();
            var ct2 = (candidate.Title ?? string.Empty).Trim();
            if (string.Equals(pn, ct2, StringComparison.OrdinalIgnoreCase))
            {
                score += 15;
                reasons.Add("precise name exact");
            }
            else if (ct2.Contains(pn, StringComparison.OrdinalIgnoreCase)
                  || pn.Contains(ct2, StringComparison.OrdinalIgnoreCase))
            {
                score += 5;
                reasons.Add("precise name contains");
            }
        }

        // NOTE: child-count scoring for TV shows (comparing ChildNames.Count against
        // numberOfSeasons) is intentionally omitted here.  TMDB's /search/tv endpoint
        // does NOT return number_of_seasons — that field is only available in the
        // /tv/{id} detail response (called from GetByIdAsync).  Attempting to read it
        // from ExtendedData during SearchAsync will always find null/absent, so the
        // bonus would never fire.  numberOfSeasons IS stored in MapTv's ExtendedData
        // so that the enriched metadata record retains it for display purposes.

        return new ScoredCandidate(candidate, score,
            reasons.Count > 0 ? string.Join(", ", reasons) : "no signals");
    }

    private static string Normalize(string s) =>
        System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"[:\-,\.']", " "),
            @"\s+", " ").Trim().ToLowerInvariant();

    // ── IMetadataProvider: get by ID ──────────────────────────────────────────

    /// <summary>
    /// Fetches full details for a specific item.
    /// The external ID format is "{type}:{tmdbId}", e.g. "movie:550" or "tv:1399".
    /// </summary>
    public async Task<MediaMetadata> GetByIdAsync(string externalId, CancellationToken ct = default)
    {
        EnsureConfigured();

        // Normalize full TMDB URLs → typed IDs before processing.
        // e.g. https://www.themoviedb.org/tv/127839-top-chef-amateurs?language=en-CA → tv:127839
        //      https://www.themoviedb.org/movie/550-fight-club → movie:550
        if (externalId.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            externalId = NormalizeTmdbUrl(externalId);

        // Supported formats:
        //   movie:{tmdbId}                        → /movie/{id}
        //   tv:{tmdbId}                           → /tv/{id}
        //   tv:{tmdbId}/season:{n}                → /tv/{id}/season/{n}
        //   tv:{tmdbId}/season:{n}/episode:{m}    → /tv/{id}/season/{n}/episode/{m}
        if (externalId.Contains("/season:", StringComparison.OrdinalIgnoreCase))
        {
            // Parse tv:{showId}/season:{n}[/episode:{m}]
            var segments = externalId.Split('/');
            var showId    = segments[0].Split(':', 2)[1];
            var seasonNum = segments[1].Split(':', 2)[1];

            if (segments.Length >= 3 && segments[2].StartsWith("episode:", StringComparison.OrdinalIgnoreCase))
            {
                var episodeNum = segments[2].Split(':', 2)[1];
                var episode = await _client!.GetTvEpisodeAsync(showId, seasonNum, episodeNum, ct).ConfigureAwait(false);
                return MapTvEpisode(episode, externalId);
            }
            else
            {
                var season = await _client!.GetTvSeasonAsync(showId, seasonNum, ct).ConfigureAwait(false);
                return MapTvSeason(season, externalId);
            }
        }

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
        ExtendedData    = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            popularity = m.Popularity,
        }),
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
        ExtendedData    = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            popularity      = t.Popularity,
            numberOfSeasons = t.NumberOfSeasons,
        }),
    };

    private static MediaMetadata MapTvSeason(TmdbSeason season, string externalId) => new()
    {
        ExternalId      = externalId,
        Source          = "tmdb",
        TotalResults    = 1,
        Title           = season.Name ?? string.Empty,
        Overview        = season.Overview,
        Year            = ParseYear(season.AirDate),
        PosterUrl       = season.PosterPath is not null
                          ? $"https://image.tmdb.org/t/p/w500{season.PosterPath}"
                          : null,
        Rating          = season.VoteAverage,
        ExtendedData    = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            seasonNumber = season.SeasonNumber,
            tmdbId       = season.Id,
            episodeCount = season.Episodes?.Count,
        }),
    };

    private static MediaMetadata MapTvEpisode(TmdbEpisode episode, string externalId) => new()
    {
        ExternalId      = externalId,
        Source          = "tmdb",
        TotalResults    = 1,
        Title           = episode.Name ?? string.Empty,
        Overview        = episode.Overview,
        Year            = ParseYear(episode.AirDate),
        PosterUrl       = episode.StillPath is not null
                          ? $"https://image.tmdb.org/t/p/w500{episode.StillPath}"
                          : null,
        Rating          = episode.VoteAverage,
        ExtendedData    = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            episodeNumber = episode.EpisodeNumber,
            seasonNumber  = episode.SeasonNumber,
            tmdbId        = episode.Id,
        }),
    };

    private static int? ParseYear(string? date) =>
        date is { Length: >= 4 } && int.TryParse(date[..4], out var y) ? y : null;

    /// <summary>
    /// Converts a full TMDB URL to a typed external ID.
    /// e.g. https://www.themoviedb.org/tv/127839-top-chef-amateurs?language=en-CA → tv:127839
    ///      https://www.themoviedb.org/movie/550-fight-club                       → movie:550
    ///      https://www.themoviedb.org/tv/3534/season/1/episode/23               → tv:3534/season:1/episode:23
    ///      https://www.themoviedb.org/tv/3534/season/1                          → tv:3534/season:1
    /// </summary>
    private static string NormalizeTmdbUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            // Path looks like:
            //   /tv/127839-some-slug
            //   /movie/550-some-slug
            //   /tv/3534-some-slug/season/1/episode/23
            //   /tv/3534-some-slug/season/1
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length >= 2)
            {
                var type = segments[0].ToLowerInvariant();   // "tv" or "movie"
                // The segment may be "127839-some-slug" — extract the leading digits
                var idPart = segments[1].Split('-')[0];
                if (type is "tv" or "movie" && int.TryParse(idPart, out _))
                {
                    // Check for /season/{n}/episode/{m} or /season/{n}
                    if (type == "tv" && segments.Length >= 4
                        && string.Equals(segments[2], "season", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(segments[3], out var seasonNum))
                    {
                        if (segments.Length >= 6
                            && string.Equals(segments[4], "episode", StringComparison.OrdinalIgnoreCase)
                            && int.TryParse(segments[5], out var episodeNum))
                        {
                            return $"tv:{idPart}/season:{seasonNum}/episode:{episodeNum}";
                        }
                        return $"tv:{idPart}/season:{seasonNum}";
                    }
                    return $"{type}:{idPart}";
                }
            }
        }
        catch { /* fall through to let GetByIdAsync handle the malformed input */ }
        return url;
    }

    private void EnsureConfigured()
    {
        if (_client is null)
            throw new InvalidOperationException("TmdbMetadataProvider has not been configured. Call Configure() first.");
    }
}
