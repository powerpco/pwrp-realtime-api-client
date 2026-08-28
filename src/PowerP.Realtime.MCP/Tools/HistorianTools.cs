using System.ComponentModel;
using ModelContextProtocol.Server;
using PowerP.Realtime.API.Client;

namespace PowerP.Realtime.MCP.Tools;

/// <summary>
/// The four operations a model needs against a plant's historian.
///
/// Descriptions carry the limits and the remedies, because a model choosing a tool reads
/// them and a model that knows a request will be refused does not send it. Refusals still
/// arrive as tool execution errors carrying the API's stable code, so the loop closes even
/// when the description was not enough.
/// </summary>
[McpServerToolType]
public class HistorianTools(PowerPAPIClient client)
{
    /// <summary>
    /// Runs a call and, when the API refuses it, returns the refusal as the tool's result
    /// instead of letting it surface as an unhandled exception.
    ///
    /// This is the whole point of the server. The specification says clients should hand
    /// tool execution errors to the model so it can correct itself, and our refusals carry
    /// a stable code, the numbers behind them and the remedy. Left unhandled, the framework
    /// replaces all of that with "An error occurred invoking the tool", and a model told
    /// only that a call failed retries the identical call.
    /// </summary>
    private static async Task<object> GuardedAsync<T>(Func<Task<T>> call, Func<T, object> shape)
    {
        try
        {
            return shape(await call());
        }
        catch (HttpRequestException ex)
        {
            return new
            {
                error = true,
                message = ex.Message,
                hint = "The message names the limit and the remedy. Adjust the arguments — "
                     + "a narrower range, a coarser resampleEvery, a smaller selection, or "
                     + "powerp_explain first — rather than repeating the same call.",
            };
        }
    }

    [McpServerTool(Name = "powerp_vocabulary", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("""
        List the tag dimensions of a bucket and the values each one takes. Call this first:
        selectors are built from these, and guessing a dimension returns nothing rather than
        an error. Cheap, and it moves no measurement data.
        """)]
    public async Task<object> VocabularyAsync(
        [Description("The bucket to inspect.")] int bucketId)
    {
        return await GuardedAsync(
            () => client.GetVocabularyAsync(bucketId),
            v => new { bucketId = v.Id, bucket = v.Bucket, signals = v.Signals, dimensions = v.Dimensions });
    }

    [McpServerTool(Name = "powerp_explain", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("""
        Price a query without running it: how many signals the selection resolves to, and
        how many points it would move. Nothing is read from the historian.

        Use this before any broad query — an empty selector, a whole site, or a range longer
        than a few hours. It is the difference between learning a request is too large in a
        few milliseconds and being refused after the server has done the work of deciding.
        """)]
    public async Task<object> ExplainAsync(
        [Description("The bucket to query.")] int bucketId,
        [Description("Tags every signal must carry, e.g. {\"level\":\"inverter\",\"signal\":\"active_power\"}. Empty matches the whole bucket.")]
        Dictionary<string, string>? selector,
        [Description("Start of the range, ISO-8601 UTC.")] DateTime startTime,
        [Description("End of the range, ISO-8601 UTC.")] DateTime endTime,
        [Description("Aggregation window such as 1m. Omit for raw points.")] string? resampleEvery = null,
        [Description("Points wanted per series; the server derives the window. Ignored when resampleEvery is given.")]
        int? maxDataPoints = null,
        [Description("Pin an exact signal set by stream key. Intersects with the selector.")]
        int[]? streamKeys = null)
    {
        return await GuardedAsync(
            () => client.QuerySelectorAsync(bucketId, selector ?? new(), startTime, endTime,
                resampleEvery, maxDataPoints, streamKeys: streamKeys, explain: true),
            r => new
        {
            plan = r.Query?.Plan,
            seriesResolved = r.Query?.SeriesRequested,
            estimatedPoints = r.Query?.EstimatedPoints,
            aggregated = r.Query?.Aggregated,
            resampleEvery = r.Query?.ResampleEvery,
            windowSource = r.Query?.WindowSource,
            unresolvedStreamKeys = r.Query?.UnresolvedStreamKeys,
        });
    }

    [McpServerTool(Name = "powerp_query", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("""
        Read measurements over a time range.

        With no resampleEvery and no maxDataPoints you get RAW points: the values as
        recorded, each keeping the instant it was measured at. Use raw whenever the timing
        is part of the answer — state changes, alarms, setpoint moves — because an
        aggregated point is stamped on its window boundary and therefore reads as having
        happened earlier than it did.

        Raw is bounded: 3 hours of range and 500 signals. Past either the call is refused,
        not quietly aggregated. Aggregated queries are bounded by a point budget. Every
        refusal names the limit and the remedy; read it and adjust rather than retrying.

        Check `aggregated` in the result before treating a timestamp as an instant. Do not
        infer it from what you asked for — the server may have derived a window.
        """)]
    public async Task<object> QueryAsync(
        [Description("The bucket to query.")] int bucketId,
        [Description("Tags every signal must carry. Empty matches the whole bucket; price that with powerp_explain first.")]
        Dictionary<string, string>? selector,
        [Description("Start of the range, ISO-8601 UTC.")] DateTime startTime,
        [Description("End of the range, ISO-8601 UTC.")] DateTime endTime,
        [Description("Aggregation window such as 1m or 5m. Omit for raw points.")] string? resampleEvery = null,
        [Description("Points wanted per series; the server derives the window and reports it.")]
        int? maxDataPoints = null,
        [Description("Aggregation to apply. Omit and each signal uses the one declared for it.")]
        string? aggFunction = null,
        [Description("Pin an exact signal set by stream key, for a reproducible set that tag changes cannot move.")]
        int[]? streamKeys = null,
        [Description("Expand status and bit-field signals into named conditions.")] bool decode = false)
    {
        return await GuardedAsync(
            () => client.QuerySelectorAsync(bucketId, selector ?? new(), startTime, endTime,
                resampleEvery, maxDataPoints, aggFunction: aggFunction, streamKeys: streamKeys,
                decode: decode),
            r => new
        {
            aggregated = r.Query?.Aggregated,
            resampleEvery = r.Query?.ResampleEvery,
            aggFunction = r.Query?.AggFunction,
            windowSource = r.Query?.WindowSource,
            estimatedPoints = r.Query?.EstimatedPoints,
            seriesReturned = r.Query?.SeriesReturned,
            unresolvedStreamKeys = r.Query?.UnresolvedStreamKeys,
            points = r.Points.Select(p => new
            {
                streamKey = p.StreamKey, tag = p.Tag, timestamp = p.Timestamp, value = p.Value,
                decoded = p.Decoded,
            }),
        });
    }

    [McpServerTool(Name = "powerp_latest", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("""
        The most recent value of every signal a selection resolves to, in one call. This is
        the right tool for "what is happening now" — it reads one point per signal rather
        than a range, so it stays cheap over a whole site.

        Timestamps are the real instants: this never aggregates.
        """)]
    public async Task<object> LatestAsync(
        [Description("The bucket to query.")] int bucketId,
        [Description("Tags every signal must carry. Empty matches the whole bucket.")]
        Dictionary<string, string>? selector,
        [Description("Pin an exact signal set by stream key.")] int[]? streamKeys = null,
        [Description("Expand status and bit-field signals into named conditions.")] bool decode = false)
    {
        return await GuardedAsync(
            () => client.QuerySelectorLatestAsync(bucketId, selector ?? new(), decode),
            r => new
        {
            seriesReturned = r.Query?.SeriesReturned,
            unresolvedStreamKeys = r.Query?.UnresolvedStreamKeys,
            points = r.Points.Select(p => new
            {
                streamKey = p.StreamKey, tag = p.Tag, timestamp = p.Timestamp, value = p.Value,
                decoded = p.Decoded,
            }),
        });
    }
}
