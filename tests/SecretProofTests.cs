using Xunit;
using System.Net;

namespace Chronicle.Plugin.TMDB.Tests;

/// <summary>The checklist's K7: a call that fails on purpose must not carry the API key out in its error text.</summary>
public class SecretProofTests
{
    private const string Key = "SECRET-KEY-1234567890";

    internal sealed class ProofHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(answer(request));
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task A_failing_call_leaves_the_api_key_out_of_the_error(HttpStatusCode status)
    {
        var client = new Chronicle.Plugin.TMDB.TmdbClient(new HttpClient(new ProofHandler(_ => new HttpResponseMessage(status))), Key, "en-US", false);
        var ex = await Record.ExceptionAsync(() => client.SearchMoviesAsync("anything"));
        Assert.NotNull(ex);
        Assert.DoesNotContain(Key, ex!.ToString());
    }
}
