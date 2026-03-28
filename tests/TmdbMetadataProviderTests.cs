using System.Net;
using System.Text;
using Chronicle.Plugin.TMDB;
using Chronicle.Plugins.Models;
using Xunit;

namespace Chronicle.Plugin.TMDB.Tests;

/// <summary>
/// Tests for TmdbMetadataProvider.SearchAsync — verifies that scored candidates are
/// returned with correct ExternalIds and that year suffixes like "(1993)" are extracted
/// from the title and passed as the TMDB primary_release_year / first_air_date_year parameter.
/// </summary>
public class TmdbMetadataProviderTests
{
    // ── SearchAsync: movie ────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_Movie_ReturnsScoredCandidateWithCorrectExternalId()
    {
        var handler = new StubHandler(req => req.RequestUri!.PathAndQuery.Contains("/search/movie")
            ? MovieSearchResponse(550, "Fight Club")
            : EmptySearchResponse());
        var provider = BuildProvider(handler);

        var results = await provider.SearchAsync(new MediaSearchContext("Fight Club"));

        Assert.NotEmpty(results);
        Assert.Equal("movie:550", results[0].Metadata.ExternalId);
    }

    [Fact]
    public async Task SearchAsync_Movie_NoResults_ReturnsEmptyList()
    {
        var handler = new StubHandler(_ => EmptySearchResponse());
        var provider = BuildProvider(handler);

        var results = await provider.SearchAsync(new MediaSearchContext("ZZZZZ_NONEXISTENT"));

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_Movie_WithYearSuffix_StripsYearFromTitle()
    {
        string? capturedMovieUrl = null;
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery.Contains("/search/movie"))
            {
                capturedMovieUrl = req.RequestUri.ToString();
                return MovieSearchResponse(12101, "Groundhog Day");
            }
            return EmptySearchResponse();
        });
        var provider = BuildProvider(handler);

        await provider.SearchAsync(new MediaSearchContext("Groundhog Day (1993)"));

        Assert.NotNull(capturedMovieUrl);
        // Title must NOT contain the year suffix
        Assert.DoesNotContain("1993", Uri.UnescapeDataString(capturedMovieUrl!.Split("query=")[1].Split("&")[0]));
        // Year must appear as the primary_release_year parameter
        Assert.Contains("primary_release_year=1993", capturedMovieUrl);
    }

    [Fact]
    public async Task SearchAsync_Movie_WithYearSuffix_ReturnsCorrectExternalId()
    {
        var handler = new StubHandler(req => req.RequestUri!.PathAndQuery.Contains("/search/movie")
            ? MovieSearchResponse(12101, "Groundhog Day")
            : EmptySearchResponse());
        var provider = BuildProvider(handler);

        var results = await provider.SearchAsync(new MediaSearchContext("Groundhog Day (1993)"));

        Assert.NotEmpty(results);
        Assert.Equal("movie:12101", results[0].Metadata.ExternalId);
    }

    [Fact]
    public async Task SearchAsync_Movie_WithContextYear_SufixStillStrippedFromTitle()
    {
        string? capturedMovieUrl = null;
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery.Contains("/search/movie"))
            {
                capturedMovieUrl = req.RequestUri.ToString();
                return MovieSearchResponse(12101, "Groundhog Day");
            }
            return EmptySearchResponse();
        });
        var provider = BuildProvider(handler);

        // Year provided via context AND in suffix — suffix is still stripped from the title
        await provider.SearchAsync(new MediaSearchContext("Groundhog Day (1993)", Year: 1993));

        Assert.NotNull(capturedMovieUrl);
        Assert.Contains("primary_release_year=1993", capturedMovieUrl);
        // "(1993)" must NOT appear in the query= segment
        Assert.DoesNotContain("1993", Uri.UnescapeDataString(capturedMovieUrl!.Split("query=")[1].Split("&")[0]));
    }

    // ── SearchAsync: TV ───────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_Tv_ReturnsScoredCandidateWithCorrectExternalId()
    {
        var handler = new StubHandler(req => req.RequestUri!.PathAndQuery.Contains("/search/tv")
            ? TvSearchResponse(1399, "Game of Thrones")
            : EmptySearchResponse());
        var provider = BuildProvider(handler);

        var results = await provider.SearchAsync(new MediaSearchContext("Game of Thrones"));

        Assert.NotEmpty(results);
        Assert.Equal("tv:1399", results[0].Metadata.ExternalId);
    }

    [Fact]
    public async Task SearchAsync_Tv_WithYearSuffix_StripsYearAndPassesParameter()
    {
        string? capturedTvUrl = null;
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery.Contains("/search/tv"))
            {
                capturedTvUrl = req.RequestUri.ToString();
                return TvSearchResponse(1396, "Breaking Bad");
            }
            return EmptySearchResponse();
        });
        var provider = BuildProvider(handler);

        await provider.SearchAsync(new MediaSearchContext("Breaking Bad (2008)"));

        Assert.NotNull(capturedTvUrl);
        Assert.DoesNotContain("2008", Uri.UnescapeDataString(capturedTvUrl!.Split("query=")[1].Split("&")[0]));
        Assert.Contains("first_air_date_year=2008", capturedTvUrl);
    }

    [Fact]
    public async Task SearchAsync_Tv_NoResults_ReturnsEmptyList()
    {
        var handler = new StubHandler(_ => EmptySearchResponse());
        var provider = BuildProvider(handler);

        var results = await provider.SearchAsync(new MediaSearchContext("ZZZZZ_NONEXISTENT"));

        Assert.Empty(results);
    }

    // ── SearchAsync: scoring ─────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_ExactTitleMatch_ScoresHigherThanContainsMatch()
    {
        // Movie results: exact match (id=1) + partial match (id=2)
        var handler = new StubHandler(req => req.RequestUri!.PathAndQuery.Contains("/search/movie")
            ? Json("""
                {
                    "results": [
                        { "id": 2, "title": "The Fight Club Chronicles", "release_date": "2000-01-01",
                          "overview": "", "poster_path": null, "backdrop_path": null },
                        { "id": 1, "title": "Fight Club", "release_date": "1999-10-15",
                          "overview": "", "poster_path": null, "backdrop_path": null }
                    ],
                    "total_results": 2, "total_pages": 1, "page": 1
                }
                """)
            : EmptySearchResponse());
        var provider = BuildProvider(handler);

        var results = await provider.SearchAsync(new MediaSearchContext("Fight Club"));

        Assert.NotEmpty(results);
        Assert.Equal("movie:1", results[0].Metadata.ExternalId);   // exact match wins
    }

    [Fact]
    public async Task SearchAsync_YearMatch_BoostsScore()
    {
        // Two identical-title TV results differing only in year
        var handler = new StubHandler(req => req.RequestUri!.PathAndQuery.Contains("/search/tv")
            ? Json("""
                {
                    "results": [
                        { "id": 100, "name": "Flash", "first_air_date": "1990-01-01",
                          "overview": "", "poster_path": null, "backdrop_path": null },
                        { "id": 200, "name": "Flash", "first_air_date": "2014-01-01",
                          "overview": "", "poster_path": null, "backdrop_path": null }
                    ],
                    "total_results": 2, "total_pages": 1, "page": 1
                }
                """)
            : EmptySearchResponse());
        var provider = BuildProvider(handler);

        var results = await provider.SearchAsync(new MediaSearchContext("Flash", Year: 2014));

        Assert.NotEmpty(results);
        Assert.Equal("tv:200", results[0].Metadata.ExternalId);    // year 2014 matches
    }

    // ── GetByIdAsync: URL normalization ──────────────────────────────────────

    [Theory]
    [InlineData("https://www.themoviedb.org/tv/127839-top-chef-amateurs?language=en-CA", "tv:127839")]
    [InlineData("https://www.themoviedb.org/tv/1399", "tv:1399")]
    [InlineData("https://www.themoviedb.org/movie/550-fight-club", "movie:550")]
    [InlineData("https://www.themoviedb.org/movie/550", "movie:550")]
    public async Task GetByIdAsync_NormalizesTmdbUrls(string inputUrl, string expectedId)
    {
        string? capturedId = null;
        var handler = new StubHandler(req =>
        {
            capturedId = req.RequestUri?.ToString();
            return TvSearchResponse(127839, "Top Chef Amateurs");
        });
        var provider = BuildProvider(handler);

        // We just care the URL is normalized — actual response doesn't matter for this test
        try { await provider.GetByIdAsync(inputUrl); } catch { /* ignore mapping errors */ }

        Assert.NotNull(capturedId);
        var type = expectedId.Split(':')[0];
        var id   = expectedId.Split(':')[1];
        // Verify the HTTP call targeted the right endpoint (not the raw URL)
        Assert.Contains($"/{type}/{id}", capturedId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TmdbMetadataProvider BuildProvider(StubHandler handler)
    {
        var http    = new HttpClient(handler);
        var client  = new TmdbClient(http, apiKey: "test_key", language: "en-US", includeAdult: false);
        return new TmdbMetadataProvider(client);
    }

    private static HttpResponseMessage MovieSearchResponse(int id, string title) =>
        Json($$$"""
            {
                "results": [{ "id": {{{id}}}, "title": "{{{title}}}", "release_date": "1993-02-12",
                              "overview": "A cynical TV weatherman covers Groundhog Day.",
                              "poster_path": null, "backdrop_path": null }],
                "total_results": 1, "total_pages": 1, "page": 1
            }
            """);

    private static HttpResponseMessage TvSearchResponse(int id, string name) =>
        Json($$$"""
            {
                "results": [{ "id": {{{id}}}, "name": "{{{name}}}", "first_air_date": "2008-01-20",
                              "overview": "A chemistry teacher breaks bad.",
                              "poster_path": null, "backdrop_path": null }],
                "total_results": 1, "total_pages": 1, "page": 1
            }
            """);

    private static HttpResponseMessage EmptySearchResponse() =>
        Json("""{"results":[],"total_results":0,"total_pages":0,"page":1}""");

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;
    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) => _factory = factory;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(_factory(request));
}
