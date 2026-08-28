using System.Net;
using System.Text;
using PowerP.Realtime.API.Client;
using Xunit;

namespace PowerP.Realtime.API.Client.Tests;

/// <summary>
/// The token lifecycle, which had none.
///
/// The client fetched once and cached forever while tokens last an hour, so any process
/// that outlived one — an integration left running, or an MCP server an AI client holds
/// open for days — started answering 401 to everything and never recovered. None of that
/// is visible in a short test run, which is why it survived: it needs a clock, or a stub
/// that can lie about one.
/// </summary>
public class TokenLifecycleTests
{
    /// <summary>Records every request and answers from a script.</summary>
    private sealed class Stub : HttpMessageHandler
    {
        public readonly List<string> Requests = [];
        public int TokensIssued;
        public int ExpiresIn = 3600;

        /// <summary>Answer this many data requests with 401 before accepting them.</summary>
        public int UnauthorizedCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(path);

            if (path.EndsWith("/auth/token"))
            {
                TokensIssued++;
                return Json($$"""
                    {"access_token":"token-{{TokensIssued}}","token_type":"Bearer","expires_in":{{ExpiresIn}}}
                    """);
            }

            if (UnauthorizedCount > 0)
            {
                UnauthorizedCount--;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("")
                });
            }

            return Json("""{"bucketId":1,"bucket":"b","signals":0,"dimensions":{}}""");
        }

        private static Task<HttpResponseMessage> Json(string body) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }

    private static PowerPAPIClient Client(Stub stub) =>
        new("https://example.invalid/rt-api/api", "id", "secret", stub);

    [Fact]
    public async Task MintsOneTokenAndReusesItWhileValid()
    {
        var stub = new Stub();
        var client = Client(stub);

        await client.GetVocabularyAsync(1);
        await client.GetVocabularyAsync(1);
        await client.GetVocabularyAsync(1);

        Assert.Equal(1, stub.TokensIssued);
    }

    [Fact]
    public async Task RefreshesWhenTheTokenIsAboutToExpire()
    {
        // Shorter than the two-minute refresh margin, so the second call must mint again.
        // This is the case that broke: an hour-long token in a process that runs for days.
        var stub = new Stub { ExpiresIn = 30 };
        var client = Client(stub);

        await client.GetVocabularyAsync(1);
        await client.GetVocabularyAsync(1);

        Assert.Equal(2, stub.TokensIssued);
    }

    [Fact]
    public async Task AMissingLifetimeIsTreatedAsShort()
    {
        // A server that omits expires_in must not be read as "never expires": refreshing
        // too often costs one request, trusting too long costs an outage.
        var stub = new Stub { ExpiresIn = 0 };
        var client = Client(stub);

        await client.GetVocabularyAsync(1);
        await client.GetVocabularyAsync(1);

        Assert.True(stub.TokensIssued >= 1);
    }

    [Fact]
    public async Task RetriesOnceWithAFreshTokenAfterA401()
    {
        // Covers what a clock cannot predict: a signing key rotated under us, or a token
        // invalidated early.
        var stub = new Stub { UnauthorizedCount = 1 };
        var client = Client(stub);

        await client.GetVocabularyAsync(1);

        Assert.Equal(2, stub.TokensIssued);
        Assert.Equal(2, stub.Requests.Count(r => r.EndsWith("/vocabulary")));
    }

    [Fact]
    public async Task ARejectedCredentialFailsRatherThanLooping()
    {
        var stub = new Stub { UnauthorizedCount = 99 };
        var client = Client(stub);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetVocabularyAsync(1));

        // One retry, not a loop: two attempts at the data, two tokens.
        Assert.Equal(2, stub.Requests.Count(r => r.EndsWith("/vocabulary")));
    }

    [Fact]
    public async Task ConcurrentCallsMintOneToken()
    {
        var stub = new Stub();
        var client = Client(stub);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.GetVocabularyAsync(1)));

        Assert.Equal(1, stub.TokensIssued);
    }

    [Theory]
    [InlineData("https://example.invalid/rt-api/api")]
    [InlineData("https://example.invalid/rt-api/api/")]
    public async Task TheApiRootIsReachedWithOrWithoutATrailingSlash(string baseUrl)
    {
        // Uri resolution against a BaseAddress without a trailing slash drops the last
        // segment, so ".../api" + "v1/auth/token" became ".../rt-api/v1/auth/token" and
        // every call 401'd. The README's own curl example omits the slash.
        var stub = new Stub();
        var client = new PowerPAPIClient(baseUrl, "id", "secret", stub);

        await client.GetVocabularyAsync(1);

        Assert.Contains("/rt-api/api/v1/auth/token", stub.Requests);
    }
}
