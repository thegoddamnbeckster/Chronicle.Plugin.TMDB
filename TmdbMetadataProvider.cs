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

    public string PluginId => "chronicle.plugin.tmdb";
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
        new MediaTypeSupport
        {
            MediaTypeName   = "movies",
            DisplayName     = "Movies",
            HierarchyLevels = 1,
            DefaultPriority = 10,
            SupportedFields = ["title", "overview", "year", "poster_url", "backdrop_url",
                               "runtime_minutes", "genres", "cast", "crew", "rating", "tags",
                               "collection"],
        },
        // Legacy alias — no DisplayName so it is not synced to the media_types table.
        new MediaTypeSupport
        {
            MediaTypeName   = "movie",
            DefaultPriority = 10,
            SupportedFields = ["title", "overview", "year", "poster_url", "backdrop_url",
                               "runtime_minutes", "genres", "cast", "crew", "rating"],
        },
        // Fan Edits are identified exclusively by the FanEdit plugin; TMDB contributes
        // movie metadata via cross-ref seeding after the FanEdit plugin locates the item.
        // Declaring "fanedits" here caused TMDB to appear in the Add Media Fan Edits search,
        // returning generic movie results that are not fan edits.
        new MediaTypeSupport
        {
            MediaTypeName    = "tv",
            DisplayName      = "TV",
            HierarchyLevels  = 3,
            HierarchyLabels  = ["Show", "Season", "Episode"],
            DefaultPriority  = 10,
            SupportedFields  = ["title", "overview", "year", "poster_url", "backdrop_url",
                                "genres", "cast", "crew", "rating", "tags"],
            LevelFields = new Dictionary<int, List<string>>
            {
                [1] = ["title", "overview", "year", "poster_url", "backdrop_url", "tags"],
                [2] = ["title", "overview", "year", "runtime_minutes", "tags"],
            },
        },
        new MediaTypeSupport
        {
            MediaTypeName    = "anime",
            DisplayName      = "Anime",
            HierarchyLevels  = 3,
            HierarchyLabels  = ["Show", "Season", "Episode"],
            DefaultPriority  = 10,
            SupportedFields  = ["title", "overview", "year", "poster_url", "backdrop_url",
                                "genres", "cast", "crew", "rating", "tags"],
            LevelFields = new Dictionary<int, List<string>>
            {
                [1] = ["title", "overview", "year", "poster_url", "backdrop_url", "tags"],
                [2] = ["title", "overview", "year", "runtime_minutes", "tags"],
            },
        },
        // Standalone anime films — flat like "movies", not hierarchical like "anime" (which is
        // Show/Season/Episode for real anime TV series). Split out so anime films are
        // collection-eligible the same way movies/fanedits are, without needing a TV-shaped
        // Season/Episode structure they don't have.
        new MediaTypeSupport
        {
            MediaTypeName   = "anime_movies",
            DisplayName     = "Anime Movies",
            HierarchyLevels = 1,
            DefaultPriority = 10,
            SupportedFields = ["title", "overview", "year", "poster_url", "backdrop_url",
                               "runtime_minutes", "genres", "cast", "crew", "rating", "tags",
                               "collection"],
        },
        // No DisplayName -- same convention as the "movie" legacy alias above: this entry does
        // NOT get synced into the media_types table, so it never registers/owns "people" (the
        // Wikipedia plugin is that type's canonical registrant, per
        // docs/plans/2026-08-28-people-section-design.md Section 1.1). TMDB only CONTRIBUTES to
        // an already-existing person -- cross-reference by ID only (SearchAsync below), never a
        // blind /search/person call, matching the design's "ID-based resolution only, no new
        // blind search" rule for every plugin (real common-name collision risk otherwise).
        new MediaTypeSupport
        {
            MediaTypeName   = "people",
            HierarchyLevels = 1,
            InteractionVerb = "viewed",
            ProgressUnit    = "percent",
            DefaultPriority = 10,
            SupportedFields = ["title", "overview", "poster_url", "birth_date", "death_date",
                               "extended_data"],
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
                Description  = "BCP 47 language tag used for titles and overviews (e.g. en-US, de-DE). " +
                                "Also used to pick a collection's poster: TMDB's own top-level pick for a " +
                                "collection doesn't reliably respect this the way it does for movies/shows " +
                                "(confirmed live -- a Turkish-market poster won over an obvious English one " +
                                "for a collection with no other English art on file), so collection posters " +
                                "are instead chosen from TMDB's full image gallery, preferring this language.",
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
        _language     = language     ?? "en-US";
    }

    private string _posterSize   = "w500";
    private string _backdropSize = "w1280";
    private string _language     = "en-US";

    // ── IMetadataProvider: search ─────────────────────────────────────────────

    // Matches a trailing " (YYYY)" or "(YYYY)" year suffix — common in file-scanner folder names.
    private static readonly System.Text.RegularExpressions.Regex YearSuffixRe =
        new(@"\s*\((\d{4})\)\s*$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Parses a string already confirmed to be Unicode decimal digits (e.g. a YearSuffixRe
    /// capture group) into an integer, digit-by-digit via
    /// CharUnicodeInfo.GetDecimalDigitValue. \d in .NET regex matches the whole Unicode Nd
    /// category, not just ASCII 0-9 — a title carrying a fullwidth year suffix (e.g. Japanese
    /// "（２０１５）") would make YearSuffixRe match "２０１５" and then int.Parse throw
    /// FormatException on it, exactly the same crash class confirmed live (2026-08-30) in
    /// Chronicle's own MetadataEnrichmentService for a fullwidth *volume number* — see
    /// Chronicle.Core.Helpers.DigitParsingHelper there for the identical fix. Duplicated here
    /// rather than referenced across the plugin boundary: this plugin only depends on the
    /// Chronicle.Plugins contract project, never Chronicle.Core, same as every other Chronicle
    /// plugin — see this repo's own manifest.json / independent versioning.
    /// </summary>
    internal static bool TryParseDigits(string digits, out int number)
    {
        number = 0;
        if (string.IsNullOrEmpty(digits)) return false;

        var accumulated = 0;
        try
        {
            checked
            {
                foreach (var c in digits)
                {
                    var digit = System.Globalization.CharUnicodeInfo.GetDecimalDigitValue(c);
                    if (digit < 0) return false; // not actually a decimal digit
                    accumulated = accumulated * 10 + digit;
                }
            }
        }
        catch (OverflowException)
        {
            return false;
        }

        number = accumulated;
        return true;
    }

    /// <summary>Minimum score for a Stage 1a result to short-circuit Stage 1b (year-less search).</summary>
    private const int ExactMatchThreshold = 60;

    /// <summary>
    /// Returns true when the media type name maps to a movie-type search endpoint.
    /// "fanedits" are movies at the source level — they reference real TMDB movie IDs.
    /// </summary>
    private static bool IsMovieType(string? mediaTypeName) =>
        mediaTypeName is null
        || mediaTypeName.Equals("movies",   StringComparison.OrdinalIgnoreCase)
        || mediaTypeName.Equals("movie",    StringComparison.OrdinalIgnoreCase)
        || mediaTypeName.Equals("fanedits", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true when the media type name maps to a TV-type search endpoint.
    /// </summary>
    private static bool IsTvType(string? mediaTypeName) =>
        mediaTypeName is null
        || mediaTypeName.Equals("tv", StringComparison.OrdinalIgnoreCase)
        || mediaTypeName.StartsWith("tv ", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<ScoredCandidate>> SearchAsync(
        MediaSearchContext context, CancellationToken ct = default)
    {
        EnsureConfigured();

        // "people" is resolved by cross-reference only -- an id recorded via
        // PersonResolutionService when this person was first credited on a TMDB-sourced title
        // (see MediaTypeSupport's own doc comment above). Deliberately no /search/person call:
        // a name-based blind search here would reintroduce exactly the common-name collision
        // risk the People feature design already chose to avoid for every plugin.
        if (string.Equals(context.MediaTypeName, "people", StringComparison.OrdinalIgnoreCase))
        {
            var personId = ExtractPersonTmdbId(context.KnownExternalIds);
            if (personId is null)
                return [];
            try
            {
                var metadata = await GetPersonWithImagesAsync(personId, ct).ConfigureAwait(false);
                return [new ScoredCandidate(metadata, 100, "cross-reference ID match")];
            }
            catch (OperationCanceledException) { throw; }
            catch (HttpRequestException) { return []; }
        }

        // Determine which TMDB endpoints to query.
        // When MediaTypeName is provided, restrict to the relevant endpoint only —
        // this prevents a movie query from matching a same-named TV show (or vice versa).
        // When MediaTypeName is null (old callers), query both for full coverage.
        bool searchMovies = IsMovieType(context.MediaTypeName);
        bool searchTv     = IsTvType(context.MediaTypeName);

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
                if (year is null && TryParseDigits(yearMatch.Groups[1].Value, out var parsedYear))
                    year = parsedYear;
                title = title[..yearMatch.Index].Trim();
            }

            if (searchMovies)
            {
                var movieResp = await _client!.SearchMoviesAsync(title, year, ct).ConfigureAwait(false);
                foreach (var m in movieResp.Results ?? [])
                    stage1aCandidates.Add(ScoreCandidate(context, MapMovie(m)));
            }

            if (searchTv)
            {
                var tvResp = await _client!.SearchTvAsync(title, year, ct).ConfigureAwait(false);
                foreach (var t in tvResp.Results ?? [])
                    stage1aCandidates.Add(ScoreCandidate(context, MapTv(t)));
            }

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

            if (searchMovies)
            {
                var movieResp = await _client!.SearchMoviesAsync(title, year: null, ct).ConfigureAwait(false);
                foreach (var m in movieResp.Results ?? [])
                    stage1bCandidates.Add(ScoreCandidate(context, MapMovie(m)));
            }

            if (searchTv)
            {
                var tvResp = await _client!.SearchTvAsync(title, year: null, ct).ConfigureAwait(false);
                foreach (var t in tvResp.Results ?? [])
                    stage1bCandidates.Add(ScoreCandidate(context, MapTv(t)));
            }
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
        //      https://www.themoviedb.org/search/movie?query=... → resolved via search, below
        if (externalId.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var resolvedFromSearch = await TryResolveSearchUrlAsync(externalId, ct).ConfigureAwait(false);
            externalId = resolvedFromSearch ?? NormalizeTmdbUrl(externalId);
        }

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
            "tv"         => await GetTvWithImagesAsync(id, ct).ConfigureAwait(false),
            "collection" => await GetCollectionWithImagesAsync(int.Parse(id), ct).ConfigureAwait(false),
            "person"     => await GetPersonWithImagesAsync(id, ct).ConfigureAwait(false),
            _            => await GetMovieWithImagesAsync(id, ct).ConfigureAwait(false),
        };
    }

    /// <summary>
    /// PersonResolutionService stores a person's cross-plugin ExternalPersonId verbatim as
    /// media_external_ids.ExternalId under Source="tmdb" -- e.g. "tmdb:36801" (the
    /// "{source}:{id}" convention docs/plans/2026-08-28-people-section-design.md Section 2
    /// defines for CastMember/CrewMember.ExternalPersonId) -- NOT the bare numeric id every
    /// other TMDB cross-reference (movie/tv/collection) stores under that same Source. Strip
    /// the redundant "tmdb:" prefix here rather than changing that storage format, which
    /// PersonResolutionService's own dedup lookup depends on matching verbatim.
    /// </summary>
    private static string? ExtractPersonTmdbId(IReadOnlyDictionary<string, string>? knownExternalIds)
    {
        if (knownExternalIds is null || !knownExternalIds.TryGetValue("tmdb", out var raw) || string.IsNullOrEmpty(raw))
            return null;
        var idx = raw.LastIndexOf(':');
        return idx >= 0 ? raw[(idx + 1)..] : raw;
    }

    // ── IMetadataProvider: episode list ───────────────────────────────────────

    public async Task<IReadOnlyList<ProviderEpisodeSummary>> GetEpisodeListAsync(
        string showExternalId, int seasonNumber, CancellationToken ct = default)
    {
        EnsureConfigured();

        // Only handles our own "tv:{id}" show ids -- anything else (a bare numeric id
        // from a different provider's namespace, a movie id, ...) isn't ours to resolve.
        var parts = showExternalId.Split(':', 2);
        if (parts.Length != 2 || !string.Equals(parts[0], "tv", StringComparison.OrdinalIgnoreCase))
            return [];

        TmdbSeason season;
        try
        {
            season = await _client!.GetTvSeasonAsync(parts[1], seasonNumber.ToString(), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Season doesn't exist upstream (e.g. asked past the show's real season count) --
            // an empty list tells the caller to stop, not that something went wrong.
            return [];
        }
        // Any OTHER failure (rate limit, network error, auth) is deliberately NOT swallowed
        // here -- a blanket catch would make the caller's "consecutive empty seasons" loop
        // misinterpret a transient provider failure as having reached the show's real season
        // count, silently giving up early. Let it propagate; ScraperController.
        // EnsureEpisodesResolvedAsync already catches and logs at the top level.

        if (season.Episodes is null) return [];

        return season.Episodes
            .Select(e => new ProviderEpisodeSummary(
                EpisodeNumber: e.EpisodeNumber,
                Title:         e.Name ?? $"Episode {e.EpisodeNumber}",
                Overview:      e.Overview,
                StillUrl:      e.StillPath is not null ? $"https://image.tmdb.org/t/p/w500{e.StillPath}" : null,
                AirDate:       e.AirDate))
            .ToList();
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

    /// <summary>
    /// Collection detail plus its full artwork list. The image call is best-effort: a
    /// collection with unreachable images is still worth returning with its parts, which are
    /// what MovieCollectionService actually needs to build the hierarchy.
    /// </summary>
    private async Task<MediaMetadata> GetCollectionWithImagesAsync(int collectionId, CancellationToken ct)
    {
        var detail = await _client!.GetCollectionAsync(collectionId, ct).ConfigureAwait(false);

        // Swallowed deliberately: artwork is supplementary, but `parts` is what
        // MovieCollectionService uses to build the hierarchy. Letting a rate-limited or flaky
        // second request take the whole collection down would turn a cosmetic gap into a
        // structural one. A failure here degrades to exactly today's behaviour — one poster.
        TmdbImageList? images = null;
        try
        {
            images = await _client.GetCollectionImagesAsync(collectionId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException) { }

        return MapCollection(detail, images);
    }

    /// <summary>
    /// Person detail plus their full photo gallery. PosterUrl comes from the detail endpoint's
    /// own profile_path (like movies/shows -- no equivalent of the collection language-pick bug
    /// has been observed for people), while AdditionalImages needs the full gallery only
    /// /person/{id}/images provides. Best-effort images call, same rationale as
    /// GetMovieWithImagesAsync/GetCollectionWithImagesAsync.
    /// </summary>
    private async Task<MediaMetadata> GetPersonWithImagesAsync(string id, CancellationToken ct)
    {
        var detail = await _client!.GetPersonAsync(id, ct).ConfigureAwait(false);

        TmdbPersonImageList? images = null;
        try
        {
            images = await _client.GetPersonImagesAsync(id, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException) { }

        return MapPerson(detail, images);
    }

    /// <summary>
    /// Movie detail plus its full artwork list. PosterUrl/BackdropUrl still come from the detail
    /// endpoint's own poster_path/backdrop_path (unlike collections, /movie/{id} reliably honors
    /// the configured language for those two fields), but that endpoint only ever returns one of
    /// each -- AdditionalImages needs the full gallery, which only /movie/{id}/images provides.
    /// Best-effort like <see cref="GetCollectionWithImagesAsync"/>: a flaky or rate-limited images
    /// call degrades to exactly today's behaviour (one poster, one backdrop) rather than failing
    /// the whole lookup.
    /// </summary>
    private async Task<MediaMetadata> GetMovieWithImagesAsync(string id, CancellationToken ct)
    {
        var detail = await _client!.GetMovieAsync(id, ct).ConfigureAwait(false);

        TmdbImageList? images = null;
        try
        {
            images = await _client.GetMovieImagesAsync(id, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException) { }

        return MapMovie(detail, images);
    }

    /// <summary>TV-show equivalent of <see cref="GetMovieWithImagesAsync"/>.</summary>
    private async Task<MediaMetadata> GetTvWithImagesAsync(string id, CancellationToken ct)
    {
        var detail = await _client!.GetTvAsync(id, ct).ConfigureAwait(false);

        TmdbImageList? images = null;
        try
        {
            images = await _client.GetTvImagesAsync(id, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException) { }

        return MapTv(detail, images);
    }

    /// <summary>
    /// Maps a TMDB collection to a <see cref="MediaMetadata"/> whose <c>Results</c> list
    /// contains one entry per collection part (movie). Used by MovieCollectionService to
    /// discover and create stub MediaItems for movies not yet in the user's library.
    /// </summary>
    private MediaMetadata MapCollection(TmdbCollection c, TmdbImageList? images = null) => new()
    {
        ExternalId  = $"collection:{c.Id}",
        Source      = "tmdb",
        Title       = c.Name,
        Overview    = c.Overview,
        // All three go through the same language-preferred/textless/null selection over the
        // full un-filtered gallery -- see SelectPreferredImageUrl. None of them use the
        // collection detail endpoint's own top-level poster_path/backdrop_path, even though
        // that request also carries &language={_language} -- confirmed live (2026-08-20/21)
        // that /collection/{id} does NOT reliably honor it the way /movie/{id} and /tv/{id} do,
        // unlike the single-poster/backdrop fields movies and shows get (see MapMovie/MapTVShow
        // below), which stay on the server-filtered field precisely because that endpoint DOES
        // honor language reliably -- there's no equivalent bug to fix there.
        PosterUrl   = SelectPreferredImageUrl(images?.Posters,   _posterSize),
        BackdropUrl = SelectPreferredImageUrl(images?.Backdrops, _backdropSize),
        LogoUrl     = SelectPreferredImageUrl(images?.Logos,     "original"),
        // Every alternate TMDB holds, so a collection can have its artwork chosen the same way
        // any other media item can. Without this a collection has exactly one poster and the
        // Additional Images gallery has nothing to offer.
        AdditionalImages = BuildAdditionalImages(images),
        Results     = c.Parts?.Select(p => new MediaMetadata
        {
            ExternalId = $"movie:{p.Id}",
            Source     = "tmdb",
            Title      = p.Title ?? string.Empty,
            Year       = ParseYear(p.ReleaseDate),
            PosterUrl  = p.PosterPath is not null ? _client!.BuildImageUrl(p.PosterPath, _posterSize) : null,
            Rating     = p.VoteAverage,
        }).ToList() ?? [],
    };

    /// <summary>
    /// Picks one image from a full TMDB image-type list (posters, backdrops, or logos) using the
    /// configured language preference, instead of trusting a detail endpoint's own single
    /// top-level pick. Confirmed live 2026-08-20/21: unlike /movie/{id} and /tv/{id}, which
    /// genuinely honor the configured `language` query parameter for their own top-level
    /// poster_path/backdrop_path, TMDB's /collection/{id} endpoint does not reliably do the same
    /// for ANY of poster_path/backdrop_path -- "The Social Network Collection" and "The Sea Beast
    /// Collection" both landed on a non-English poster (Turkish, Spanish-market) purely because
    /// that happened to be the top overall vote, with a perfectly good English poster sitting
    /// unused in the same gallery. This is why collections re-select from the full,
    /// deliberately-unfiltered gallery (GetCollectionImagesAsync) client-side instead, while
    /// movies/shows/seasons/episodes stay on their own detail endpoint's server-filtered field --
    /// there's no equivalent bug to work around there.
    ///
    /// Preference order: an exact match for the configured language (highest-voted among those)
    /// -&gt; textless/universal art (no burned-in text to be in the "wrong" language) -&gt; null.
    ///
    /// Deliberately null, not "highest vote regardless of language" or a detail endpoint's own
    /// unreliable pick, when neither of the above exists -- confirmed live 2026-08-21 that for
    /// some collections TMDB's ENTIRE gallery is a single non-English image with nothing else on
    /// file, so "highest vote overall" always just re-selected that same wrong-language image on
    /// every rebuild, no matter how the language preference was applied. Returning null instead
    /// lets MovieCollectionService.PersistCollectionMetadataAsync clear a stale value and fall
    /// back to something better (a member movie's own poster, for PosterUrl specifically) rather
    /// than this plugin insisting on an unwanted image just because it's the only one TMDB has.
    /// </summary>
    private string? SelectPreferredImageUrl(List<TmdbImage>? images, string size)
    {
        if (images is not { Count: > 0 }) return null;

        var preferredLanguage = _language.Split('-')[0];

        var best =
            images.Where(i => string.Equals(i.Language, preferredLanguage, StringComparison.OrdinalIgnoreCase))
                  .OrderByDescending(i => i.VoteAverage).ThenByDescending(i => i.VoteCount).FirstOrDefault()
            ?? images.Where(i => i.Language is null)
                  .OrderByDescending(i => i.VoteAverage).ThenByDescending(i => i.VoteCount).FirstOrDefault();

        return best is not null && !string.IsNullOrWhiteSpace(best.FilePath)
            ? _client!.BuildImageUrl(best.FilePath, size)
            : null;
    }

    /// <summary>
    /// Flattens TMDB's per-kind image lists into the generic AdditionalImage pool. Types are
    /// the lowercase slot vocabulary the frontend's TYPE_TO_SLOT table already understands
    /// ("poster", "backdrop", "logo"), so artwork from any /images gallery (collection, movie,
    /// or TV show) becomes promotable with no frontend change.
    /// </summary>
    private List<AdditionalImage> BuildAdditionalImages(TmdbImageList? images)
    {
        if (images is null) return [];

        var result = new List<AdditionalImage>();
        Add(images.Posters,   "poster");
        Add(images.Backdrops, "backdrop");
        Add(images.Logos,     "logo");
        return result;

        void Add(List<TmdbImage>? list, string type)
        {
            if (list is null) return;
            // Best first: community score, then vote count as the tiebreak, so the gallery
            // opens on the artwork most people picked rather than an arbitrary upload order.
            foreach (var img in list.OrderByDescending(i => i.VoteAverage).ThenByDescending(i => i.VoteCount))
            {
                if (string.IsNullOrWhiteSpace(img.FilePath)) continue;
                result.Add(new AdditionalImage
                {
                    // Full size for the viewer, w500 for the thumbnail grid.
                    Url          = _client!.BuildImageUrl(img.FilePath, "original"),
                    ThumbnailUrl = _client!.BuildImageUrl(img.FilePath, "w500"),
                    Type         = type,
                });
            }
        }
    }

    private MediaMetadata MapMovie(TmdbMovie m, TmdbImageList? images = null) => new()
    {
        ExternalId      = $"movie:{m.Id}",
        Source          = "tmdb",
        Title           = m.Title,
        Overview        = m.Overview,
        Year            = ParseYear(m.ReleaseDate),
        PosterUrl       = m.PosterPath   is not null ? _client!.BuildImageUrl(m.PosterPath,   _posterSize)   : null,
        BackdropUrl     = m.BackdropPath is not null ? _client!.BuildImageUrl(m.BackdropPath, _backdropSize) : null,
        // Only populated by GetMovieWithImagesAsync (the GetByIdAsync path) -- SearchAsync maps
        // bare search results, which don't carry a gallery and don't need one.
        AdditionalImages = BuildAdditionalImages(images),
        RuntimeMinutes  = m.Runtime,
        Rating          = m.VoteAverage,
        Genres          = m.Genres?.Select(g => g.Name).ToList() ?? [],
        Cast            = m.Credits?.Cast?.OrderBy(c => c.Order).Select(c => new CastMember(
                              c.Name, c.Character,
                              ExternalPersonId: $"tmdb:{c.Id}",
                              ProfileImageUrl: c.ProfilePath is null ? null : _client!.BuildImageUrl(c.ProfilePath, "h632")))
                              .Take(10).ToList() ?? [],
        Crew            = m.Credits?.Crew?.Select(c => new CrewMember(
                              c.Name, c.Job,
                              ExternalPersonId: $"tmdb:{c.Id}",
                              ProfileImageUrl: c.ProfilePath is null ? null : _client!.BuildImageUrl(c.ProfilePath, "h632")))
                              .ToList() ?? [],
        ExtendedData    = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            popularity = m.Popularity,
            belongsToCollection = m.BelongsToCollection is null ? null : new
            {
                id           = m.BelongsToCollection.Id,
                name         = m.BelongsToCollection.Name,
                posterPath   = m.BelongsToCollection.PosterPath is not null
                                  ? _client!.BuildImageUrl(m.BelongsToCollection.PosterPath, _posterSize) : null,
                backdropPath = m.BelongsToCollection.BackdropPath is not null
                                  ? _client!.BuildImageUrl(m.BelongsToCollection.BackdropPath, _backdropSize) : null,
            },
            tagline       = m.Tagline,
            status        = m.Status,
            homepage      = m.Homepage,
            released      = m.ReleaseDate,
            country       = m.ProductionCountries?.Select(c => c.Name).FirstOrDefault(n => n is not null),
            language      = m.SpokenLanguages?.Select(l => l.EnglishName).FirstOrDefault(n => n is not null),
            certification = FindUsCertification(m.ReleaseDates),
            trailer       = FindTrailerUrl(m.Videos),
            studio        = m.ProductionCompanies?.Select(c => c.Name).FirstOrDefault(n => n is not null),
            ids           = new { tmdb = m.Id, imdb = m.ImdbId },
        }),
    };

    /// <summary>US theatrical/digital certification (e.g. "PG-13") from the release_dates
    /// append_to_response block -- falls back to the first non-empty certification from any
    /// country if the US entry has none, rather than leaving MPAA blank when data exists.</summary>
    private static string? FindUsCertification(TmdbReleaseDatesResult? releaseDates)
    {
        var countries = releaseDates?.Results;
        if (countries is null) return null;

        var us = countries.FirstOrDefault(c => c.Iso3166_1 == "US");
        var usCert = us?.ReleaseDates?.Select(r => r.Certification).FirstOrDefault(c => !string.IsNullOrEmpty(c));
        if (!string.IsNullOrEmpty(usCert)) return usCert;

        return countries
            .SelectMany(c => c.ReleaseDates ?? [])
            .Select(r => r.Certification)
            .FirstOrDefault(c => !string.IsNullOrEmpty(c));
    }

    /// <summary>Official YouTube trailer URL from the videos append_to_response block, or the
    /// first YouTube trailer if none is flagged official.</summary>
    private static string? FindTrailerUrl(TmdbVideosResult? videos)
    {
        var candidates = (videos?.Results ?? [])
            .Where(v => v.Site == "YouTube" && v.Type == "Trailer" && !string.IsNullOrEmpty(v.Key))
            .ToList();
        var best = candidates.FirstOrDefault(v => v.Official) ?? candidates.FirstOrDefault();
        return best is null ? null : $"https://www.youtube.com/watch?v={best.Key}";
    }

    private MediaMetadata MapTv(TmdbTv t, TmdbImageList? images = null) => new()
    {
        ExternalId      = $"tv:{t.Id}",
        Source          = "tmdb",
        Title           = t.Name,
        Overview        = t.Overview,
        Year            = ParseYear(t.FirstAirDate),
        PosterUrl       = t.PosterPath   is not null ? _client!.BuildImageUrl(t.PosterPath,   _posterSize)   : null,
        BackdropUrl     = t.BackdropPath is not null ? _client!.BuildImageUrl(t.BackdropPath, _backdropSize) : null,
        // Only populated by GetTvWithImagesAsync (the GetByIdAsync path) -- SearchAsync maps
        // bare search results, which don't carry a gallery and don't need one.
        AdditionalImages = BuildAdditionalImages(images),
        RuntimeMinutes  = t.EpisodeRunTime?.FirstOrDefault(),
        Rating          = t.VoteAverage,
        Genres          = t.Genres?.Select(g => g.Name).ToList() ?? [],
        Cast            = t.Credits?.Cast?.OrderBy(c => c.Order).Select(c => new CastMember(
                              c.Name, c.Character,
                              ExternalPersonId: $"tmdb:{c.Id}",
                              ProfileImageUrl: c.ProfilePath is null ? null : _client!.BuildImageUrl(c.ProfilePath, "h632")))
                              .Take(10).ToList() ?? [],
        Crew            = t.Credits?.Crew?.Select(c => new CrewMember(
                              c.Name, c.Job,
                              ExternalPersonId: $"tmdb:{c.Id}",
                              ProfileImageUrl: c.ProfilePath is null ? null : _client!.BuildImageUrl(c.ProfilePath, "h632")))
                              .ToList() ?? [],
        ExtendedData    = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            popularity      = t.Popularity,
            numberOfSeasons = t.NumberOfSeasons,
            tagline       = t.Tagline,
            status        = t.Status,
            homepage      = t.Homepage,
            first_aired   = t.FirstAirDate,
            network       = t.Networks?.Select(n => n.Name).FirstOrDefault(n => n is not null),
            country       = t.ProductionCountries?.Select(c => c.Name).FirstOrDefault(n => n is not null),
            language      = t.SpokenLanguages?.Select(l => l.EnglishName).FirstOrDefault(n => n is not null),
            certification = FindUsContentRating(t.ContentRatings),
            trailer       = FindTrailerUrl(t.Videos),
            ids           = new { tmdb = t.Id, imdb = t.ExternalIds?.ImdbId, tvdb = t.ExternalIds?.TvdbId },
        }),
    };

    private MediaMetadata MapPerson(TmdbPerson p, TmdbPersonImageList? images = null) => new()
    {
        ExternalId       = $"person:{p.Id}",
        Source           = "tmdb",
        Title            = p.Name,
        Overview         = p.Biography,
        PosterUrl        = p.ProfilePath is not null ? _client!.BuildImageUrl(p.ProfilePath, _posterSize) : null,
        // Only populated by GetPersonWithImagesAsync (the GetByIdAsync/cross-reference
        // SearchAsync path) -- tagged "poster" (not a new "profile"/"headshot" type) so it
        // slots directly into the existing poster gallery/pin UI with zero frontend changes.
        AdditionalImages = BuildProfileImages(images),
        // birthDate/deathDate match MetadataResolutionService.FieldMap's keys verbatim (see
        // Chronicle.Plugin.Wikipedia's own BuildExtendedData for the same convention/name).
        ExtendedData     = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            birthDate = p.Birthday,
            deathDate = p.Deathday,
            ids       = new { tmdb = p.Id },
        }),
    };

    /// <summary>
    /// Flattens TMDB's person-images gallery (a single flat "profiles" list, unlike the
    /// posters/backdrops/logos split BuildAdditionalImages handles) into the generic
    /// AdditionalImage pool, all tagged "poster" -- a person's headshot IS their poster-slot
    /// artwork. Best-first by community score, same as BuildAdditionalImages.
    /// </summary>
    private List<AdditionalImage> BuildProfileImages(TmdbPersonImageList? images)
    {
        if (images?.Profiles is not { Count: > 0 } profiles) return [];

        var result = new List<AdditionalImage>();
        foreach (var img in profiles.OrderByDescending(i => i.VoteAverage).ThenByDescending(i => i.VoteCount))
        {
            if (string.IsNullOrWhiteSpace(img.FilePath)) continue;
            result.Add(new AdditionalImage
            {
                Url          = _client!.BuildImageUrl(img.FilePath, "original"),
                ThumbnailUrl = _client!.BuildImageUrl(img.FilePath, "w500"),
                Type         = "poster",
            });
        }
        return result;
    }

    /// <summary>US TV content rating (e.g. "TV-MA") from the content_ratings append_to_response
    /// block -- falls back to the first non-empty rating from any country if the US entry
    /// has none.</summary>
    private static string? FindUsContentRating(TmdbContentRatingsResult? contentRatings)
    {
        var countries = contentRatings?.Results;
        if (countries is null) return null;

        var us = countries.FirstOrDefault(c => c.Iso3166_1 == "US")?.Rating;
        if (!string.IsNullOrEmpty(us)) return us;

        return countries.Select(c => c.Rating).FirstOrDefault(r => !string.IsNullOrEmpty(r));
    }

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
    /// Resolves a TMDB *search results* URL (e.g. https://www.themoviedb.org/search/movie?query=...)
    /// to a concrete movie:/tv: external ID by running the query through the TMDB search endpoint
    /// and taking the top hit. Fix Match users routinely copy this URL straight out of their
    /// browser's address bar after searching, rather than clicking into the specific title first --
    /// that URL never resolves to one item on its own, so without this Fix Match always throws
    /// "Unrecognised TMDB content type 'search'" no matter how many times the same link is retried.
    /// Returns null for any URL that isn't a /search/{movie|tv} path (movie/tv page URLs, and
    /// genuinely malformed URLs, fall through unchanged to NormalizeTmdbUrl).
    /// </summary>
    private async Task<string?> TryResolveSearchUrlAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (!uri.Host.Equals("www.themoviedb.org", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.Equals("themoviedb.org", StringComparison.OrdinalIgnoreCase))
            return null;

        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2 || !segments[0].Equals("search", StringComparison.OrdinalIgnoreCase))
            return null;

        var contentType = segments[1].ToLowerInvariant();
        if (contentType is not "movie" and not "tv") return null;

        string? query = null;
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("query", StringComparison.OrdinalIgnoreCase))
            {
                query = Uri.UnescapeDataString(kv[1].Replace("+", " "));
                break;
            }
        }
        if (string.IsNullOrWhiteSpace(query)) return null;

        if (contentType == "movie")
        {
            var results = await _client!.SearchMoviesAsync(query, ct: ct).ConfigureAwait(false);
            var top = results.Results?.FirstOrDefault();
            return top is not null ? $"movie:{top.Id}" : null;
        }
        else
        {
            var results = await _client!.SearchTvAsync(query, ct: ct).ConfigureAwait(false);
            var top = results.Results?.FirstOrDefault();
            return top is not null ? $"tv:{top.Id}" : null;
        }
    }

    /// <summary>
    /// Converts a full TMDB URL to a typed external ID.
    /// e.g. https://www.themoviedb.org/tv/127839-top-chef-amateurs?language=en-CA → tv:127839
    ///      https://www.themoviedb.org/movie/550-fight-club                       → movie:550
    ///      https://www.themoviedb.org/tv/3534/season/1/episode/23               → tv:3534/season:1/episode:23
    ///      https://www.themoviedb.org/tv/3534/season/1                          → tv:3534/season:1
    ///      https://www.themoviedb.org/collection/8864-final-destination-collection → collection:8864
    /// </summary>
    private static string NormalizeTmdbUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Invalid TMDB URL: '{url}'");

        if (!uri.Host.Equals("www.themoviedb.org", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.Equals("themoviedb.org", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"URL is not a themoviedb.org address: '{url}'");

        // Path looks like:
        //   /tv/127839-some-slug
        //   /movie/550-some-slug
        //   /tv/3534-some-slug/season/1/episode/23
        //   /tv/3534-some-slug/season/1
        //   /collection/8864-some-slug
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2)
            throw new ArgumentException(
                $"Cannot extract content type and ID from TMDB URL: '{url}'. " +
                "Expected /movie/{{id}} or /tv/{{id}}.");

        var type = segments[0].ToLowerInvariant();
        if (type is not "tv" and not "movie" and not "collection")
            throw new ArgumentException(
                $"Unrecognised TMDB content type '{type}' in URL: '{url}'. Expected /movie/, /tv/, or /collection/.");

        // The segment may be "127839-some-slug" — extract the leading numeric portion.
        var idPart = segments[1].Split('-')[0];
        if (!int.TryParse(idPart, out _))
            throw new ArgumentException(
                $"Cannot extract a numeric TMDB ID from URL segment '{segments[1]}' in: '{url}'");

        // Check for /season/{n}/episode/{m} or /season/{n}
        if (type == "tv" && segments.Length >= 4
            && string.Equals(segments[2], "season", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(segments[3], out var seasonNum))
        {
            if (segments.Length >= 6
                && string.Equals(segments[4], "episode", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(segments[5], out var episodeNum))
                return $"tv:{idPart}/season:{seasonNum}/episode:{episodeNum}";
            return $"tv:{idPart}/season:{seasonNum}";
        }
        return $"{type}:{idPart}";
    }

    private void EnsureConfigured()
    {
        if (_client is null)
            throw new Chronicle.Plugins.PluginAuthException(
                "chronicle.plugin.tmdb",
                "TMDB plugin is not configured — set an API key in Settings → Plugins → TMDB.");
    }
}
