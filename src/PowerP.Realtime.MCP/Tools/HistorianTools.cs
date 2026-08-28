using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PowerP.Realtime.API.Client;
using PowerP.Realtime.API.Client.DTO;

namespace PowerP.Realtime.MCP.Tools;

/// <summary>
/// The operations a model needs against a plant's historian. All of them read; none writes.
///
/// Descriptions carry the limits and the remedies, because a model choosing a tool reads
/// them, and one that knows a request will be refused does not send it. The limits are
/// enforced by the server regardless — the description saves a round trip, it is not the
/// control.
/// </summary>
[McpServerToolType]
public class HistorianTools(PowerPAPIClient client)
{
    private const string ReadOnlyTool = "read-only";

    [McpServerTool(Name = "powerp_buckets", ReadOnly = true, Idempotent = true,
                   Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("""
        List the buckets this credential can read, with their ids. Start here when you do
        not already know a bucketId: every other tool needs one and there is no way to guess
        it. Returns only what your credential is scoped to.
        """)]
    public Task<BucketsResult> BucketsAsync() =>
        Guarded(() => client.GetBucketsAsync(),
                bs => new BucketsResult(bs.Select(b => new BucketSummary(b.Id, b.Name)).ToList()));

    [McpServerTool(Name = "powerp_vocabulary", ReadOnly = true, Idempotent = true,
                   Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("""
        List the tag dimensions of a bucket and the values each one takes. Call this before
        building a selector: selectors are made of these, and a guessed dimension matches
        nothing rather than returning an error. Reads no measurement data.
        """)]
    public Task<VocabularyResult> VocabularyAsync(
        [Description("The bucket to inspect.")] int bucketId) =>
        Guarded(() => client.GetVocabularyAsync(bucketId),
                v => new VocabularyResult(v.Id, v.Bucket, v.Signals, v.Dimensions));

    [McpServerTool(Name = "powerp_explain", ReadOnly = true, Idempotent = true,
                   Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("""
        Price a query without running it: how many signals the selection resolves to and how
        many points it would move. Reads nothing from the historian.

        Use it before anything broad — an empty selector, a whole site, a range longer than a
        few hours. It is the difference between learning a request is too large in
        milliseconds and being refused after the server has done the work of deciding.
        """)]
    public Task<PlanResult> ExplainAsync(
        [Description("The bucket to query.")] int bucketId,
        [Description("Tags every signal must carry, e.g. {\"level\":\"inverter\",\"signal\":\"active_power\"}. Empty matches the whole bucket.")]
        Dictionary<string, string>? selector,
        [Description("Start of the range, ISO-8601 UTC.")] DateTime startTime,
        [Description("End of the range, ISO-8601 UTC.")] DateTime endTime,
        [Description("Aggregation window such as 1m. Omit for raw points.")] string? resampleEvery = null,
        [Description("Points wanted per series; the server derives the window. Ignored when resampleEvery is given.")]
        int? maxDataPoints = null,
        [Description("Pin an exact signal set by stream key. Intersects with the selector.")]
        int[]? streamKeys = null) =>
        Guarded(
            () => client.QuerySelectorAsync(bucketId, selector ?? new(), startTime, endTime,
                resampleEvery, maxDataPoints, streamKeys: streamKeys, explain: true),
            r => new PlanResult(r.Query?.Plan, r.Query?.SeriesRequested, r.Query?.EstimatedPoints,
                r.Query?.Aggregated, r.Query?.ResampleEvery, r.Query?.WindowSource,
                r.Query?.UnresolvedStreamKeys));

    [McpServerTool(Name = "powerp_query", ReadOnly = true, Idempotent = true,
                   Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("""
        Read measurements over a time range.

        With no resampleEvery and no maxDataPoints you get RAW points: the values as
        recorded, each keeping the instant it was measured at. Use raw whenever the timing is
        part of the answer — state changes, alarms, setpoint moves — because an aggregated
        point is stamped on its window boundary and so reads as having happened earlier than
        it did.

        Raw is capped at 3 hours and 500 signals; past either the call is refused, not
        quietly aggregated. Aggregated queries are capped by a point budget. Every refusal
        names the limit and the remedy: read it and adjust rather than retrying.

        Check `aggregated` in the result before treating a timestamp as an instant. Do not
        infer it from what you asked for — the server may have derived a window.
        """)]
    public Task<QueryResult> QueryAsync(
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
        [Description("Pin an exact signal set by stream key, for a set that tag changes cannot move.")]
        int[]? streamKeys = null,
        [Description("Expand status and bit-field signals into named conditions.")] bool decode = false) =>
        Guarded(
            () => client.QuerySelectorAsync(bucketId, selector ?? new(), startTime, endTime,
                resampleEvery, maxDataPoints, aggFunction: aggFunction, streamKeys: streamKeys,
                decode: decode),
            r => new QueryResult(r.Query?.Aggregated ?? false, r.Query?.ResampleEvery,
                r.Query?.AggFunction, r.Query?.WindowSource, r.Query?.EstimatedPoints,
                r.Query?.SeriesReturned ?? 0, r.Query?.UnresolvedStreamKeys, Shape(r)));

    [McpServerTool(Name = "powerp_latest", ReadOnly = true, Idempotent = true,
                   Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("""
        The most recent value of every signal a selection resolves to, in one call. The right
        tool for "what is happening now": it reads one point per signal rather than a range,
        so it stays cheap over a whole site.

        Timestamps are real instants; this never aggregates.
        """)]
    public Task<LatestResult> LatestAsync(
        [Description("The bucket to query.")] int bucketId,
        [Description("Tags every signal must carry. Empty matches the whole bucket.")]
        Dictionary<string, string>? selector,
        [Description("Expand status and bit-field signals into named conditions.")] bool decode = false) =>
        Guarded(
            () => client.QuerySelectorLatestAsync(bucketId, selector ?? new(), decode),
            r => new LatestResult(r.Query?.SeriesReturned ?? 0, r.Query?.UnresolvedStreamKeys, Shape(r)));

    private static IReadOnlyList<PointResult> Shape(SelectorQueryResponse r) =>
        r.Points.Select(p => new PointResult(p.StreamKey, p.Tag, p.Timestamp, p.Value, p.Decoded))
                .ToList();

    /// <summary>
    /// Runs a call and turns a refusal into a tool execution error the model can act on.
    ///
    /// The specification says clients should hand tool execution errors to the model so it
    /// can self-correct; the API's refusals already name the limit, the numbers and the
    /// remedy. Left to the framework's default, all of that becomes "An error occurred
    /// invoking the tool", and a model told only that something failed retries the identical
    /// call. Throwing keeps the success path a declared type, so each tool still publishes
    /// an outputSchema.
    /// </summary>
    private static async Task<TOut> Guarded<TIn, TOut>(Func<Task<TIn>> call, Func<TIn, TOut> shape)
    {
        try
        {
            return shape(await call());
        }
        catch (HttpRequestException ex)
        {
            throw new McpException(
                $"{ex.Message} Adjust the arguments — a narrower range, a coarser "
                + "resampleEvery, a smaller selection, or powerp_explain first — rather than "
                + "repeating this call unchanged.");
        }
    }
}
