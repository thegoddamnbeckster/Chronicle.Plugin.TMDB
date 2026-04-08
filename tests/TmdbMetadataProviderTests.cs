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

    // ── SearchAsync: AltTitles cascade ────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_UsesAltTitles_TriesEachTitle()
    {
        // Name returns empty; the FilenameStem alt-title should be tried and succeed.
        var titlesSearched = new List<string>();
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery.Contains("/search/movie"))
            {
                var query = Uri.UnescapeDataString(
                    req.RequestUri.Query.Split("query=")[1].Split("&")[0]);
                titlesSearched.Add(query);
                // Return a hit only for the stem title
                return query == "Fight Club"
                    ? MovieSearchResponse(550, "Fight Club")
                    : EmptySearchResponse();
            }
            return EmptySearchResponse();
        });
        var provider = BuildProvider(handler);

        // Name has no results; FilenameStem "Fight Club" is the second alt-title
        var ctx = new MediaSearchContext(
            Name: "Fight Club (Director's Cut)",
            AltTitles: ["Fight Club (Director's Cut)", "Fight Club"]);

        var results = await provider.SearchAsync(ctx);

        Assert.NotEmpty(results);
        Assert.Equal("movie:550", results[0].Metadata.ExternalId);
        // Both alt-titles must have been tried (exact equality — not a substring check)
        Assert.Contains(titlesSearched, t => t == "Fight Club (Director's Cut)");
        Assert.Contains(titlesSearched, t => t == "Fight Club");
    }

    [Fact]
    public async Task SearchAsync_Stage1b_DropsYear_WhenNoHighScoreInStage1a()
    {
        // Stage 1a returns a low-score result; Stage 1b should retry without year.
        var yearlessRequests = new List<string>();
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (path.Contains("/search/movie"))
            {
                // Track requests that have NO primary_release_year
                if (!path.Contains("primary_release_year"))
                    yearlessRequests.Add(path);

                // Low-confidence result: title mismatch → score < 60
                return Json("""
                    {
                        "results": [{ "id": 99, "title": "Something Else Entirely",
                                      "release_date": "2010-01-01",
                                      "overview": "", "poster_path": null, "backdrop_path": null }],
                        "total_results": 1, "total_pages": 1, "page": 1
                    }
                    """);
            }
            return EmptySearchResponse();
        });
        var provider = BuildProvider(handler);

        var ctx = new MediaSearchContext(
            Name: "MyTitle",
            Year: 2010,
            AltTitles: ["MyTitle"]);

        await provider.SearchAsync(ctx);

        // Stage 1b must have fired a request without the year parameter
        Assert.NotEmpty(yearlessRequests);
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

    [Fact]
    public async Task ScoreCandidate_YearMismatch_AppliesPenalty()
    {
        // Two movies: id=1 has title+year exact, id=2 has title exact but year off by 3.
        // id=2 should score lower due to the -10 year-mismatch penalty.
        var handler = new StubHandler(req => req.RequestUri!.PathAndQuery.Contains("/search/movie")
            ? Json("""
                {
                    "results": [
                        { "id": 1, "title": "Duplicate Title", "release_date": "2010-01-01",
                          "overview": "", "poster_path": null, "backdrop_path": null },
                        { "id": 2, "title": "Duplicate Title", "release_date": "2000-01-01",
                          "overview": "", "poster_path": null, "backdrop_path": null }
                    ],
                    "total_results": 2, "total_pages": 1, "page": 1
                }
                """)
            : EmptySearchResponse());
        var provider = BuildProvider(handler);

        // Context year = 2010; id=1 matches exactly (+20), id=2 is off by 10 years (-10)
        var results = await provider.SearchAsync(new MediaSearchContext("Duplicate Title", Year: 2010));

        Assert.NotEmpty(results);
        Assert.Equal("movie:1", results[0].Metadata.ExternalId);  // year-mismatch-penalised id=2 loses
        // id=2 must be present but ranked lower
        Assert.True(results[0].Score > results.First(r => r.Metadata.ExternalId == "movie:2").Score);
    }

    // ── GetByIdAsync: URL normalization ──────────────────────────────────────

    [Theory]
    [InlineData("https://www.themoviedb.org/tv/127839-top-chef-amateurs?language=en-CA", "/tv/127839")]
    [InlineData("https://www.themoviedb.org/tv/1399", "/tv/1399")]
    [InlineData("https://www.themoviedb.org/movie/550-fight-club", "/movie/550")]
    [InlineData("https://www.themoviedb.org/movie/550", "/movie/550")]
    public async Task GetByIdAsync_NormalizesTmdbUrls(string inputUrl, string expectedPathSegment)
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
        // Verify the HTTP call targeted the right endpoint (not the raw URL)
        Assert.Contains(expectedPathSegment, capturedId);
    }

    [Theory]
    [InlineData("https://www.themoviedb.org/tv/3534-space-above-and-beyond/season/1/episode/23", "/tv/3534/season/1/episode/23")]
    [InlineData("https://www.themoviedb.org/tv/3534/season/1/episode/23", "/tv/3534/season/1/episode/23")]
    [InlineData("https://www.themoviedb.org/tv/3534/season/2", "/tv/3534/season/2")]
    public async Task GetByIdAsync_NormalizesTmdbEpisodeSeasonUrls(string inputUrl, string expectedPathSegment)
    {
        string? capturedPath = null;
        var handler = new StubHandler(req =>
        {
            capturedPath = req.RequestUri?.PathAndQuery;
            // Return minimal episode/season response to avoid mapping errors
            return Json("""{ "id": 1, "name": "Test", "overview": "", "air_date": "1995-01-01", "episodes": [] }""");
        });
        var provider = BuildProvider(handler);

        try { await provider.GetByIdAsync(inputUrl); } catch { /* ignore mapping errors */ }

        Assert.NotNull(capturedPath);
        Assert.Contains(expectedPathSegment, capturedPath);
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
