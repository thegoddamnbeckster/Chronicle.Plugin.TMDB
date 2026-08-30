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
    [property: JsonPropertyName("id")]                    int       Id,
    [property: JsonPropertyName("title")]                 string    Title,
    [property: JsonPropertyName("overview")]              string?   Overview,
    [property: JsonPropertyName("release_date")]          string?   ReleaseDate,
    [property: JsonPropertyName("poster_path")]           string?   PosterPath,
    [property: JsonPropertyName("backdrop_path")]         string?   BackdropPath,
    [property: JsonPropertyName("runtime")]               int?      Runtime,
    [property: JsonPropertyName("vote_average")]          double?   VoteAverage,
    [property: JsonPropertyName("popularity")]            double?   Popularity,
    [property: JsonPropertyName("genres")]                List<TmdbGenre>?  Genres,
    [property: JsonPropertyName("credits")]               TmdbCredits?      Credits,
    [property: JsonPropertyName("belongs_to_collection")] TmdbBelongsToCollection? BelongsToCollection,
    [property: JsonPropertyName("tagline")]               string?   Tagline,
    [property: JsonPropertyName("status")]                string?   Status,
    [property: JsonPropertyName("homepage")]               string?   Homepage,
    [property: JsonPropertyName("imdb_id")]                string?   ImdbId,
    [property: JsonPropertyName("production_countries")]  List<TmdbCountry>?  ProductionCountries,
    [property: JsonPropertyName("spoken_languages")]       List<TmdbLanguage>? SpokenLanguages,
    [property: JsonPropertyName("release_dates")]          TmdbReleaseDatesResult? ReleaseDates,
    [property: JsonPropertyName("videos")]                 TmdbVideosResult?       Videos,
    [property: JsonPropertyName("production_companies")]  List<TmdbCompany>?      ProductionCompanies
);

internal record TmdbCompany(
    [property: JsonPropertyName("name")] string? Name
);

internal record TmdbCountry(
    [property: JsonPropertyName("iso_3166_1")] string? Iso3166_1,
    [property: JsonPropertyName("name")]       string? Name
);

internal record TmdbLanguage(
    [property: JsonPropertyName("iso_639_1")]    string? Iso639_1,
    [property: JsonPropertyName("english_name")] string? EnglishName
);

internal record TmdbReleaseDatesResult(
    [property: JsonPropertyName("results")] List<TmdbReleaseDatesCountry>? Results
);

internal record TmdbReleaseDatesCountry(
    [property: JsonPropertyName("iso_3166_1")]   string  Iso3166_1,
    [property: JsonPropertyName("release_dates")] List<TmdbReleaseDateEntry>? ReleaseDates
);

internal record TmdbReleaseDateEntry(
    [property: JsonPropertyName("certification")] string? Certification
);

internal record TmdbVideosResult(
    [property: JsonPropertyName("results")] List<TmdbVideo>? Results
);

internal record TmdbVideo(
    [property: JsonPropertyName("key")]      string? Key,
    [property: JsonPropertyName("site")]     string? Site,
    [property: JsonPropertyName("type")]     string? Type,
    [property: JsonPropertyName("official")] bool    Official
);

internal record TmdbBelongsToCollection(
    [property: JsonPropertyName("id")]            int     Id,
    [property: JsonPropertyName("name")]          string  Name,
    [property: JsonPropertyName("poster_path")]   string? PosterPath,
    [property: JsonPropertyName("backdrop_path")] string? BackdropPath
);

// ── TV show ───────────────────────────────────────────────────────────────────

internal record TmdbTv(
    [property: JsonPropertyName("id")]               int       Id,
    [property: JsonPropertyName("name")]             string    Name,
    [property: JsonPropertyName("overview")]         string?   Overview,
    [property: JsonPropertyName("first_air_date")]   string?   FirstAirDate,
    [property: JsonPropertyName("poster_path")]      string?   PosterPath,
    [property: JsonPropertyName("backdrop_path")]    string?   BackdropPath,
    [property: JsonPropertyName("episode_run_time")] List<int>? EpisodeRunTime,
    [property: JsonPropertyName("vote_average")]     double?   VoteAverage,
    [property: JsonPropertyName("popularity")]       double?   Popularity,
    [property: JsonPropertyName("number_of_seasons")] int?    NumberOfSeasons,
    [property: JsonPropertyName("genres")]           List<TmdbGenre>? Genres,
    [property: JsonPropertyName("credits")]          TmdbCredits?     Credits,
    [property: JsonPropertyName("tagline")]          string?   Tagline,
    [property: JsonPropertyName("status")]           string?   Status,
    [property: JsonPropertyName("homepage")]         string?   Homepage,
    [property: JsonPropertyName("networks")]         List<TmdbNetwork>? Networks,
    [property: JsonPropertyName("production_countries")] List<TmdbCountry>?  ProductionCountries,
    [property: JsonPropertyName("spoken_languages")]      List<TmdbLanguage>? SpokenLanguages,
    [property: JsonPropertyName("content_ratings")]  TmdbContentRatingsResult? ContentRatings,
    [property: JsonPropertyName("videos")]           TmdbVideosResult?         Videos,
    [property: JsonPropertyName("external_ids")]     TmdbTvExternalIds?        ExternalIds
);

internal record TmdbNetwork(
    [property: JsonPropertyName("name")] string? Name
);

internal record TmdbContentRatingsResult(
    [property: JsonPropertyName("results")] List<TmdbContentRatingCountry>? Results
);

internal record TmdbContentRatingCountry(
    [property: JsonPropertyName("iso_3166_1")] string  Iso3166_1,
    [property: JsonPropertyName("rating")]     string? Rating
);

internal record TmdbTvExternalIds(
    [property: JsonPropertyName("imdb_id")] string? ImdbId,
    [property: JsonPropertyName("tvdb_id")] int?    TvdbId
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
    [property: JsonPropertyName("id")]         int Id,
    [property: JsonPropertyName("name")]       string Name,
    [property: JsonPropertyName("character")]  string? Character,
    [property: JsonPropertyName("order")]      int Order,
    [property: JsonPropertyName("profile_path")] string? ProfilePath
);

internal record TmdbCrewMember(
    [property: JsonPropertyName("id")]         int Id,
    [property: JsonPropertyName("name")]       string Name,
    [property: JsonPropertyName("job")]        string? Job,
    [property: JsonPropertyName("department")] string? Department,
    [property: JsonPropertyName("profile_path")] string? ProfilePath
);

// ── Collection detail ──────────────────────────────────────────────────────────

internal sealed class TmdbCollection
{
    [JsonPropertyName("id")]            public int Id { get; set; }
    [JsonPropertyName("name")]          public string Name { get; set; } = string.Empty;
    [JsonPropertyName("overview")]      public string? Overview { get; set; }
    [JsonPropertyName("poster_path")]   public string? PosterPath { get; set; }
    [JsonPropertyName("backdrop_path")] public string? BackdropPath { get; set; }
    [JsonPropertyName("parts")]         public List<TmdbCollectionPart>? Parts { get; set; }
}

internal sealed class TmdbCollectionPart
{
    [JsonPropertyName("id")]            public int Id { get; set; }
    [JsonPropertyName("title")]         public string? Title { get; set; }
    [JsonPropertyName("release_date")]  public string? ReleaseDate { get; set; }
    [JsonPropertyName("poster_path")]   public string? PosterPath { get; set; }
    [JsonPropertyName("vote_average")]  public double? VoteAverage { get; set; }
}

// ── Image lists ────────────────────────────────────────────────────────────────

/// <summary>Response of the /images endpoints. TMDB carries dozens of alternates per
/// collection (81 posters for Die Hard, 100 for Fast &amp; Furious) that the detail
/// endpoint's single poster_path never exposes.</summary>
internal sealed class TmdbImageList
{
    [JsonPropertyName("posters")]   public List<TmdbImage>? Posters { get; set; }
    [JsonPropertyName("backdrops")] public List<TmdbImage>? Backdrops { get; set; }
    [JsonPropertyName("logos")]     public List<TmdbImage>? Logos { get; set; }
}

internal sealed class TmdbImage
{
    [JsonPropertyName("file_path")]    public string FilePath { get; set; } = string.Empty;
    [JsonPropertyName("width")]        public int Width { get; set; }
    [JsonPropertyName("height")]       public int Height { get; set; }
    /// <summary>Language of any burned-in text; null for textless artwork.</summary>
    [JsonPropertyName("iso_639_1")]    public string? Language { get; set; }
    [JsonPropertyName("vote_average")] public double VoteAverage { get; set; }
    [JsonPropertyName("vote_count")]   public int VoteCount { get; set; }
}
