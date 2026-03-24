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

// ── TV Season ─────────────────────────────────────────────────────────────────

internal sealed class TmdbSeason
{
    [JsonPropertyName("id")]             public int Id { get; set; }
    [JsonPropertyName("season_number")]  public int SeasonNumber { get; set; }
    [JsonPropertyName("name")]           public string? Name { get; set; }
    [JsonPropertyName("overview")]       public string? Overview { get; set; }
    [JsonPropertyName("air_date")]       public string? AirDate { get; set; }
    [JsonPropertyName("poster_path")]    public string? PosterPath { get; set; }
    [JsonPropertyName("vote_average")]   public double VoteAverage { get; set; }
    [JsonPropertyName("episodes")]       public List<TmdbEpisode>? Episodes { get; set; }
}

// ── TV Episode ────────────────────────────────────────────────────────────────

internal sealed class TmdbEpisode
{
    [JsonPropertyName("id")]              public int Id { get; set; }
    [JsonPropertyName("episode_number")]  public int EpisodeNumber { get; set; }
    [JsonPropertyName("season_number")]   public int SeasonNumber { get; set; }
    [JsonPropertyName("name")]            public string? Name { get; set; }
    [JsonPropertyName("overview")]        public string? Overview { get; set; }
    [JsonPropertyName("air_date")]        public string? AirDate { get; set; }
    [JsonPropertyName("still_path")]      public string? StillPath { get; set; }
    [JsonPropertyName("vote_average")]    public double VoteAverage { get; set; }
    [JsonPropertyName("show_id")]         public int ShowId { get; set; }
}

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
