using System.Net;
using System.Text;
using Chronicle.Plugin.TMDB;
using Xunit;

namespace Chronicle.Plugin.TMDB.Tests;

/// <summary>
/// Tests for TmdbMetadataProvider.SearchAsync — verifies that the best match is
/// promoted to the top-level ExternalId (so the enrichment service can record it)
/// and that year suffixes like "(1993)" are extracted from the title and passed as
/// the TMDB primary_release_year / first_air_date_year parameter.
/// </summary>
public class TmdbMetadataProviderTests
{
    // ── SearchAsync: movie ────────────────────────────────────────────────────

    /// <summary>
    /// The enrichment service checks result.ExternalId to decide whether a match
    /// was found. Without promotion the container MediaMetadata has ExternalId=null
    /// and every search is permanently marked NotFound.
    /// </summary>
    [Fact]
    public async Task SearchAsync_Movie_PromotesBestResultExternalIdToTopLevel()
    {
        var handler = new StubHandler(_ => MovieSearchResponse(550, "Fight Club"));
        var provider = BuildProvider(handler);

        var result = await provider.SearchAsync("Fight Club", "movies");

        Assert.NotNull(result.ExternalId);
        Assert.Equal("movie:550", result.ExternalId);
    }

    [Fact]
    public async Task SearchAsync_Movie_NoResults_ReturnsNullExternalId()
    {
        var handler = new StubHandler(_ => EmptySearchResponse());
        var provider = BuildProvider(handler);

        var result = await provider.SearchAsync("ZZZZZ_NONEXISTENT", "movies");

        Assert.Null(result.ExternalId);
    }

    [Fact]
    public async Task SearchAsync_Movie_WithYearSuffix_StripsYearFromTitle()
    {
        string? capturedUrl = null;
        var handler = new StubHandler(req =>
        {
            capturedUrl = req.RequestUri?.ToString();
            return MovieSearchResponse(12101, "Groundhog Day");
        });
        var provider = BuildProvider(handler);

        await provider.SearchAsync("Groundhog Day (1993)", "movies");

        Assert.NotNull(capturedUrl);
        // Title must NOT contain the year suffix
        Assert.DoesNotContain("1993", Uri.UnescapeDataString(capturedUrl!.Split("query=")[1].Split("&")[0]));
        // Year must appear as the primary_release_year parameter
        Assert.Contains("primary_release_year=1993", capturedUrl);
    }

    [Fact]
    public async Task SearchAsync_Movie_WithYearSuffix_ReturnsCorrectExternalId()
    {
        var handler = new StubHandler(_ => MovieSearchResponse(12101, "Groundhog Day"));
        var provider = BuildProvider(handler);

        var result = await provider.SearchAsync("Groundhog Day (1993)", "movies");

        Assert.Equal("movie:12101", result.ExternalId);
    }

    // ── SearchAsync: TV ───────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_Tv_PromotesBestResultExternalIdToTopLevel()
    {
        var handler = new StubHandler(_ => TvSearchResponse(1399, "Game of Thrones"));
        var provider = BuildProvider(handler);

        var result = await provider.SearchAsync("Game of Thrones", "tv");

        Assert.NotNull(result.ExternalId);
        Assert.Equal("tv:1399", result.ExternalId);
    }

    [Fact]
    public async Task SearchAsync_Tv_WithYearSuffix_StripsYearAndPassesParameter()
    {
        string? capturedUrl = null;
        var handler = new StubHandler(req =>
        {
            capturedUrl = req.RequestUri?.ToString();
            return TvSearchResponse(1396, "Breaking Bad");
        });
        var provider = BuildProvider(handler);

        await provider.SearchAsync("Breaking Bad (2008)", "tv");

        Assert.NotNull(capturedUrl);
        Assert.DoesNotContain("2008", Uri.UnescapeDataString(capturedUrl!.Split("query=")[1].Split("&")[0]));
        Assert.Contains("first_air_date_year=2008", capturedUrl);
    }

    [Fact]
    public async Task SearchAsync_Tv_NoResults_ReturnsNullExternalId()
    {
        var handler = new StubHandler(_ => EmptySearchResponse());
        var provider = BuildProvider(handler);

        var result = await provider.SearchAsync("ZZZZZ_NONEXISTENT", "tv");

        Assert.Null(result.ExternalId);
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
