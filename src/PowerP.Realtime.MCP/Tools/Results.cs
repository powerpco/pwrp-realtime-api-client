using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerP.Realtime.MCP.Tools;

/// <summary>
/// The shapes the tools return.
///
/// Declared as types rather than anonymous objects so the server can publish an
/// outputSchema for each tool. Without one a client receives structured content it cannot
/// validate and a model has to infer the shape from an example — which is the same guessing
/// that made a caller believe an aggregated timestamp was an instant.
/// </summary>
public record BucketSummary(int Id, string Name);

public record BucketsResult(
    [property: JsonPropertyName("buckets")] IReadOnlyList<BucketSummary> Buckets);

public record VocabularyResult(
    int BucketId,
    string? Bucket,
    int Signals,
    Dictionary<string, List<string>> Dimensions);

/// <summary>What a query would cost, without running it.</summary>
public record PlanResult(
    string? Plan,
    int? SeriesResolved,
    /// <summary>Points the query would move. Null when the points would be raw.</summary>
    long? EstimatedPoints,
    bool? Aggregated,
    string? ResampleEvery,
    string? WindowSource,
    IReadOnlyList<int>? UnresolvedStreamKeys);

public record PointResult(
    int StreamKey,
    string? Tag,
    DateTime Timestamp,
    double Value,
    /// <summary>Named conditions, present only for status signals read with decode.</summary>
    JsonElement? Decoded);

public record QueryResult(
    /// <summary>False means the timestamps are the instants the values were recorded at.
    /// True means they are window boundaries. Read this rather than assuming from the
    /// request: the server may have derived a window.</summary>
    bool Aggregated,
    string? ResampleEvery,
    string? AggFunction,
    string? WindowSource,
    long? EstimatedPoints,
    int SeriesReturned,
    IReadOnlyList<int>? UnresolvedStreamKeys,
    IReadOnlyList<PointResult> Points);

public record LatestResult(
    int SeriesReturned,
    IReadOnlyList<int>? UnresolvedStreamKeys,
    IReadOnlyList<PointResult> Points);
