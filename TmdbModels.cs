using System.Text.Json.Serialization;

namespace Chronicle.Plugin.TMDB;

// ── Search results ────────────────────────────────────────────────────────────

internal record TmdbSearchResponse<T>(
    [property: JsonPropertyName("results")]      List<T>  Results,
    [property: JsonPropertyName("total_results")] int     TotalResults,
    [property: JsonPropertyName("total_pages")]   int     TotalPages,
    [property: JsonPropertyName("page")]          int     Page
);

// ── Movie ─────────────────────────────────────────────────────────────────────

internal record TmdbMovie(
    [property: JsonPropertyName("id")]             int       Id,
    [property: JsonPropertyName("title")]          string    Title,
    [property: JsonPropertyName("overview")]       string?   Overview,
    [property: JsonPropertyName("release_date")]   string?   ReleaseDate,
    [property: JsonPropertyName("poster_path")]    string?   PosterPath,
    [property: JsonPropertyName("backdrop_path")]  string?   BackdropPath,
    [property: JsonPropertyName("runtime")]        int?      Runtime,
    [property: JsonPropertyName("vote_average")]   double?   VoteAverage,
    [property: JsonPropertyName("genres")]         List<TmdbGenre>? Genres,
    [property: JsonPropertyName("credits")]        TmdbCredits?     Credits
);

// ── TV show ───────────────────────────────────────────────────────────────────

internal record TmdbTv(
    [property: JsonPropertyName("id")]              int       Id,
    [property: JsonPropertyName("name")]            string    Name,
    [property: JsonPropertyName("overview")]        string?   Overview,
    [property: JsonPropertyName("first_air_date")]  string?   FirstAirDate,
    [property: JsonPropertyName("poster_path")]     string?   PosterPath,
    [property: JsonPropertyName("backdrop_path")]   string?   BackdropPath,
    [property: JsonPropertyName("episode_run_time")] List<int>? EpisodeRunTime,
    [property: JsonPropertyName("vote_average")]    double?   VoteAverage,
    [property: JsonPropertyName("genres")]          List<TmdbGenre>? Genres,
    [property: JsonPropertyName("credits")]         TmdbCredits?     Credits
);

// ── Shared sub-types ─────────────────────────────────────────────────────────

internal record TmdbGenre(
    [property: JsonPropertyName("id")]   int    Id,
    [property: JsonPropertyName("name")] string Name
);

internal record TmdbCredits(
    [property: JsonPropertyName("cast")] List<TmdbCastMember>? Cast,
    [property: JsonPropertyName("crew")] List<TmdbCrewMember>? Crew
);

internal record TmdbCastMember(
    [property: JsonPropertyName("name")]       string Name,
    [property: JsonPropertyName("character")]  string? Character,
    [property: JsonPropertyName("order")]      int Order
);

internal record TmdbCrewMember(
    [property: JsonPropertyName("name")]       string Name,
    [property: JsonPropertyName("job")]        string? Job,
    [property: JsonPropertyName("department")] string? Department
);
