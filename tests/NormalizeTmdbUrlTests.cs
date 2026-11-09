using Xunit;

namespace Chronicle.Plugin.TMDB.Tests;

public class NormalizeTmdbUrlTests
{
    [Theory]
    [InlineData("https://www.themoviedb.org/person/8265-john-michael-higgins", "person:8265")]
    [InlineData("https://www.themoviedb.org/person/8265", "person:8265")]
    [InlineData("https://www.themoviedb.org/person/8265-john-michael-higgins?language=en-CA", "person:8265")]
    [InlineData("https://www.themoviedb.org/movie/550-fight-club", "movie:550")]
    [InlineData("https://www.themoviedb.org/tv/3534-some-show/season/1/episode/23", "tv:3534/season:1/episode:23")]
    [InlineData("https://www.themoviedb.org/collection/8864-final-destination-collection", "collection:8864")]
    public void ConvertsAPageUrlToATypedId(string url, string expected) =>
        Assert.Equal(expected, TmdbMetadataProvider.NormalizeTmdbUrl(url));

    [Fact]
    public void RefusesAContentTypeItDoesNotKnow()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            TmdbMetadataProvider.NormalizeTmdbUrl("https://www.themoviedb.org/company/420-marvel-studios"));
        Assert.Contains("/person/", ex.Message);
    }
}
