using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Xunit;

namespace Chronicle.Plugin.TMDB.Tests;

/// <summary>
/// Tests for TmdbClient's 429 retry handling — added after a review found TMDB had zero
/// rate-limit handling of any kind (every call went straight through EnsureSuccessStatusCode).
/// TMDB's real limit is a short rolling window, not a daily quota like SIMKL's, so this is a
/// single short bounded retry, not a SIMKL-style multi-hour cutoff — see MaxRetryAfterWait's
/// own doc comment in TmdbClient.cs.
/// </summary>
public class TmdbClientTests
{
    [Fact]
    public async Task SearchMoviesAsync_429ThenSuccess_RetriesOnceAndReturnsResult()
    {
        var callCount = 0;
        var handler = new StubHandler(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                resp.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(1));
                return resp;
            }
            return Json("""{"results":[{"id":550,"title":"Fight Club","release_date":"1999-10-15","overview":"","poster_path":null,"backdrop_path":null}],"total_results":1,"total_pages":1,"page":1}""");
        });

        var client = new TmdbClient(new HttpClient(handler), apiKey: "test_key", language: "en-US", includeAdult: false);
        var result = await client.SearchMoviesAsync("Fight Club");

        Assert.Equal(2, callCount);
        Assert.Single(result.Results!);
        Assert.Equal(550, result.Results![0].Id);
    }

    [Fact]
    public async Task SearchMoviesAsync_PersistentRateLimit_StopsAfterOneRetryAndThrows()
    {
        var callCount = 0;
        var handler = new StubHandler(_ =>
        {
            callCount++;
            var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            resp.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(1));
            return resp;
        });

        var client = new TmdbClient(new HttpClient(handler), apiKey: "test_key", language: "en-US", includeAdult: false);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchMoviesAsync("Fight Club"));

        // Bounded to exactly one retry (2 total calls) — not an unbounded loop that would
        // keep hammering TMDB, and not zero retries either (that was the pre-fix behavior).
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task SearchMoviesAsync_NoRateLimit_MakesExactlyOneCall()
    {
        var callCount = 0;
        var handler = new StubHandler(_ =>
        {
            callCount++;
            return Json("""{"results":[],"total_results":0,"total_pages":0,"page":1}""");
        });

        var client = new TmdbClient(new HttpClient(handler), apiKey: "test_key", language: "en-US", includeAdult: false);
        await client.SearchMoviesAsync("Anything");

        Assert.Equal(1, callCount);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
