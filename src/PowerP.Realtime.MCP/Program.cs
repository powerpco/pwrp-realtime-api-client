using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PowerP.Realtime.API.Client;

namespace PowerP.Realtime.MCP;

/// <summary>
/// An MCP server over the PowerP Real-Time API, so a model queries a plant's historian
/// through a typed, bounded set of operations instead of composing HTTP by hand.
///
/// The point is not to expose the four calls. It is that the limits and the remedies live
/// in the tool contract: each tool says what it costs and what will refuse it, and every
/// refusal comes back as a tool execution error carrying the server's own code and the
/// numbers behind it — which the specification says clients should hand to the model
/// precisely so it can correct itself rather than retry the same request in a loop.
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // stdio is the transport: the server runs as a subprocess of the AI client, which
        // holds the credential. Nothing new is exposed to the network, and a tenant's
        // secret never leaves the machine that already has it.
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        var baseUrl = Env("POWERP_BASE_URL");
        var clientId = Env("POWERP_CLIENT_ID");
        var clientSecret = Env("POWERP_CLIENT_SECRET");

        builder.Services.AddSingleton(new PowerPAPIClient(baseUrl, clientId, clientSecret));
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        await builder.Build().RunAsync();
    }

    /// <summary>
    /// Configuration is required, not defaulted. A server that starts without a credential
    /// only fails later, inside a tool call, where the model reads it as the plant being
    /// unreachable rather than as its operator having misconfigured the server.
    /// </summary>
    private static string Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"{name} is not set. The server needs POWERP_BASE_URL, POWERP_CLIENT_ID and " +
                "POWERP_CLIENT_SECRET; see the README for a client configuration example.");
}
