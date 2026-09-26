using Xunit;

namespace Chronicle.Plugin.TMDB.Tests;

public class PersonCreditsTests
{
    private static string Img(string path, string size) => $"img/{size}{path}";

    [Fact]
    public void MapsCastAsActorAndCrewByJob_MoviesAndTv_WithYear()
    {
        var credits = new TmdbCombinedCredits
        {
            Cast =
            [
                new() { Id = 603, MediaType = "movie", Title = "The Matrix", ReleaseDate = "1999-03-30", PosterPath = "/m.jpg", Character = "Neo" },
                new() { Id = 1399, MediaType = "tv", Name = "Some Show", FirstAirDate = "2011-04-17", Character = "" },
            ],
            Crew = [new() { Id = 604, MediaType = "movie", Title = "Reloaded", ReleaseDate = "2003-05-15", Job = "Producer" }],
        };

        var result = TmdbMetadataProvider.MapPersonCredits(credits, Img);

        Assert.Equal(3, result.Count);
        Assert.All(result, r => Assert.Equal("tmdb", r.Source));
        Assert.Contains(result, r => r.ExternalId == "movie:603" && r.Role == "Actor" && r.Year == 1999 && r.CharacterName == "Neo" && r.PosterUrl == "img/w342/m.jpg");
        Assert.Contains(result, r => r.ExternalId == "tv:1399" && r.Role == "Actor" && r.Year == 2011 && r.CharacterName is null);
        Assert.Contains(result, r => r.ExternalId == "movie:604" && r.Role == "Producer");
    }

    [Fact]
    public void DuplicateRoleOnTheSameTitleIsListedOnce_AndBlankTitlesAndPeopleAreSkipped()
    {
        var credits = new TmdbCombinedCredits
        {
            Cast =
            [
                new() { Id = 1, MediaType = "movie", Title = "A", Character = "X" },
                new() { Id = 1, MediaType = "movie", Title = "A", Character = "Y" },
                new() { Id = 2, MediaType = "movie", Title = " " },
                new() { Id = 3, MediaType = "person", Name = "Z" },
            ],
        };

        var result = TmdbMetadataProvider.MapPersonCredits(credits, Img);

        Assert.Single(result);
    }
}
