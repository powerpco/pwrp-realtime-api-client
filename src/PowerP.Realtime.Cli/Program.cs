using System.CommandLine;
using System.Globalization;
using PowerP.Realtime.API.Client;

namespace PowerP.Realtime.Cli;

/// <summary>
/// A command-line client for the PowerP Real-Time API.
///
/// Same library, same limits and same refusals as every other path — this is a shell
/// around the client, not a second implementation with its own opinions.
///
/// Table output goes to stdout and its explanatory header to stderr, so piping to a file
/// or to jq yields data and nothing else, while a person still sees whether they are
/// holding raw points or window boundaries.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var baseUrl = new Option<string?>("--base-url") { Description = "API root, e.g. https://acme.powerp.app/rt-api/api. Defaults to POWERP_BASE_URL." };
        var clientId = new Option<string?>("--client-id") { Description = "Client id. Defaults to POWERP_CLIENT_ID." };
        var secret = new Option<string?>("--secret") { Description = "Client secret. Prefer --secret-file or POWERP_CLIENT_SECRET_FILE: an argument is visible in the process list and in shell history." };
        var secretFile = new Option<string?>("--secret-file") { Description = "File holding the secret. Defaults to POWERP_CLIENT_SECRET_FILE." };
        var format = new Option<Format>("--format", "-f") { Description = "table, json or csv.", DefaultValueFactory = _ => Format.Table };

        var bucket = new Option<int>("--bucket", "-b") { Description = "Bucket id. Run 'powerp buckets' if you do not know it.", Required = true };
        var selector = new Option<string[]>("--selector", "-s") { Description = "Tag filter as key=value; repeat for several. Empty matches the whole bucket.", AllowMultipleArgumentsPerToken = true };
        var keys = new Option<int[]>("--keys") { Description = "Pin an exact signal set by stream key.", AllowMultipleArgumentsPerToken = true };
        var from = new Option<DateTime?>("--from") { Description = "Start of the range (ISO-8601 UTC)." };
        var to = new Option<DateTime?>("--to") { Description = "End of the range (ISO-8601 UTC). Defaults to now." };
        var last = new Option<string?>("--last") { Description = "Range as a duration back from now, e.g. 30m or 6h. Simpler than --from/--to." };
        var every = new Option<string?>("--every") { Description = "Aggregation window, e.g. 1m. Omit for raw points." };
        var points = new Option<int?>("--points") { Description = "Points wanted per series; the server derives the window." };
        var agg = new Option<string?>("--agg") { Description = "Aggregation to apply. Omit and each signal uses its own." };
        var decode = new Option<bool>("--decode") { Description = "Expand status and bit-field signals into named conditions." };

        var root = new RootCommand("Query a plant's historian through the PowerP Real-Time API.");
        foreach (var o in new Option[] { baseUrl, clientId, secret, secretFile, format })
        {
            // Recursive, so they can be given on the subcommand where a person naturally
            // types them: `powerp query -b 1 -f csv`, not `powerp -f csv query -b 1`.
            o.Recursive = true;
            root.Options.Add(o);
        }

        // ---------------------------------------------------------------- buckets
        var bucketsCmd = new Command("buckets", "List the buckets this credential can read.");
        bucketsCmd.SetAction(async (parse, _) => await Run(parse, async client =>
        {
            var buckets = await client.GetBucketsAsync();
            if (parse.GetValue(format) == Format.Json) { Output.Json(buckets); return; }
            Console.WriteLine($"{"ID",-6} NAME");
            foreach (var b in buckets) Console.WriteLine($"{b.Id,-6} {b.Name}");
        }, baseUrl, clientId, secret, secretFile));
        root.Subcommands.Add(bucketsCmd);

        // ------------------------------------------------------------- vocabulary
        var vocabCmd = new Command("vocabulary", "List a bucket's tag dimensions and their values.");
        vocabCmd.Options.Add(bucket);
        vocabCmd.SetAction(async (parse, _) => await Run(parse, async client =>
        {
            var v = await client.GetVocabularyAsync(parse.GetValue(bucket));
            if (parse.GetValue(format) == Format.Json) { Output.Json(v); return; }
            Console.Error.WriteLine($"# {v.Bucket}: {v.Signals} signals");
            foreach (var (dim, values) in v.Dimensions.OrderBy(d => d.Key))
                Console.WriteLine($"{dim,-14} {string.Join(", ", values)}");
        }, baseUrl, clientId, secret, secretFile));
        root.Subcommands.Add(vocabCmd);

        // ---------------------------------------------------------------- explain
        var explainCmd = new Command("explain", "Price a query without running it.");
        foreach (var o in new Option[] { bucket, selector, keys, from, to, last, every, points }) explainCmd.Options.Add(o);
        explainCmd.SetAction(async (parse, _) => await Run(parse, async client =>
        {
            var (start, stop) = Window(parse, from, to, last);
            var r = await client.QuerySelectorAsync(parse.GetValue(bucket), Tags(parse, selector),
                start, stop, parse.GetValue(every), parse.GetValue(points),
                streamKeys: Keys(parse, keys), explain: true);

            if (parse.GetValue(format) == Format.Json) { Output.Json(r.Query); return; }
            var q = r.Query;
            Console.WriteLine($"plan             {q?.Plan}");
            Console.WriteLine($"series           {q?.SeriesRequested:N0}");
            Console.WriteLine($"estimated points {(q?.EstimatedPoints is { } e ? e.ToString("N0") : "raw — not predictable")}");
            Console.WriteLine($"window           {(q?.Aggregated == true ? $"{q.ResampleEvery} ({q.WindowSource})" : "none, raw points")}");
        }, baseUrl, clientId, secret, secretFile));
        root.Subcommands.Add(explainCmd);

        // ------------------------------------------------------------------ query
        var queryCmd = new Command("query", "Read measurements over a time range.");
        foreach (var o in new Option[] { bucket, selector, keys, from, to, last, every, points, agg, decode }) queryCmd.Options.Add(o);
        queryCmd.SetAction(async (parse, _) => await Run(parse, async client =>
        {
            var (start, stop) = Window(parse, from, to, last);
            var r = await client.QuerySelectorAsync(parse.GetValue(bucket), Tags(parse, selector),
                start, stop, parse.GetValue(every), parse.GetValue(points),
                aggFunction: parse.GetValue(agg), streamKeys: Keys(parse, keys),
                decode: parse.GetValue(decode));
            Output.Points(r, parse.GetValue(format));
        }, baseUrl, clientId, secret, secretFile));
        root.Subcommands.Add(queryCmd);

        // ----------------------------------------------------------------- latest
        var latestCmd = new Command("latest", "The current value of every signal a selection resolves to.");
        foreach (var o in new Option[] { bucket, selector, decode }) latestCmd.Options.Add(o);
        latestCmd.SetAction(async (parse, _) => await Run(parse, async client =>
        {
            var r = await client.QuerySelectorLatestAsync(parse.GetValue(bucket),
                Tags(parse, selector), parse.GetValue(decode));
            Output.Points(r, parse.GetValue(format));
        }, baseUrl, clientId, secret, secretFile));
        root.Subcommands.Add(latestCmd);

        return await root.Parse(args).InvokeAsync();
    }

    /// <summary>
    /// Builds the client, runs the command, and turns a refusal into a message and a
    /// non-zero exit code rather than a stack trace. The API names the limit and the
    /// remedy; a script needs the status, a person needs the sentence.
    /// </summary>
    private static async Task<int> Run(
        ParseResult parse, Func<PowerPAPIClient, Task> body,
        Option<string?> baseUrl, Option<string?> clientId,
        Option<string?> secret, Option<string?> secretFile)
    {
        try
        {
            var url = parse.GetValue(baseUrl) ?? Environment.GetEnvironmentVariable("POWERP_BASE_URL");
            var id = parse.GetValue(clientId) ?? Environment.GetEnvironmentVariable("POWERP_CLIENT_ID");
            var key = ResolveSecret(parse.GetValue(secret), parse.GetValue(secretFile));

            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(key))
            {
                Console.Error.WriteLine(
                    "Missing credentials. Set POWERP_BASE_URL, POWERP_CLIENT_ID and either " +
                    "POWERP_CLIENT_SECRET or POWERP_CLIENT_SECRET_FILE, or pass --base-url, " +
                    "--client-id and --secret-file.");
                return 2;
            }

            await body(new PowerPAPIClient(url, id, key));
            return 0;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 3;
        }
    }

    private static string? ResolveSecret(string? inline, string? file)
    {
        var path = file ?? Environment.GetEnvironmentVariable("POWERP_CLIENT_SECRET_FILE");
        if (!string.IsNullOrWhiteSpace(path))
            // Trimmed: a file written by an editor carries a newline, and a secret with one
            // appended fails authentication for a reason nobody can see.
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;

        return inline ?? Environment.GetEnvironmentVariable("POWERP_CLIENT_SECRET");
    }

    /// <summary>
    /// Null when no key was given, rather than an empty array.
    ///
    /// An option of array type parses to an empty array when absent, and the API treats an
    /// empty pin as "these signals and no others" — correctly, since that is what an ingest
    /// whose key list came back empty means. Sending one unasked selected nothing.
    /// </summary>
    private static int[]? Keys(ParseResult parse, Option<int[]> keys) =>
        parse.GetValue(keys) is { Length: > 0 } k ? k : null;

    private static Dictionary<string, string> Tags(ParseResult parse, Option<string[]> selector)
    {
        var tags = new Dictionary<string, string>();
        foreach (var pair in parse.GetValue(selector) ?? [])
        {
            var i = pair.IndexOf('=');
            if (i <= 0)
                throw new ArgumentException($"--selector expects key=value, got '{pair}'.");
            tags[pair[..i]] = pair[(i + 1)..];
        }
        return tags;
    }

    /// <summary>
    /// --last is the common case and --from/--to the precise one. Defaulting to a bounded
    /// window rather than to everything keeps an unqualified command cheap.
    /// </summary>
    private static (DateTime Start, DateTime Stop) Window(
        ParseResult parse, Option<DateTime?> from, Option<DateTime?> to, Option<string?> last)
    {
        var stop = parse.GetValue(to) ?? DateTime.UtcNow;

        if (parse.GetValue(last) is { Length: > 0 } duration)
        {
            var unit = duration[^1];
            if (!int.TryParse(duration[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n <= 0)
                throw new ArgumentException($"--last expects a duration such as 30m, 6h or 2d, got '{duration}'.");
            var span = unit switch
            {
                's' => TimeSpan.FromSeconds(n),
                'm' => TimeSpan.FromMinutes(n),
                'h' => TimeSpan.FromHours(n),
                'd' => TimeSpan.FromDays(n),
                _ => throw new ArgumentException($"--last expects s, m, h or d, got '{unit}'."),
            };
            return (stop - span, stop);
        }

        return (parse.GetValue(from) ?? stop.AddHours(-1), stop);
    }
}
