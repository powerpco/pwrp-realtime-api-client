using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerP.Realtime.API.Client.DTO;

/// <summary>The result of a v2 selector query: the plan the server ran and the points.</summary>
public class SelectorQueryResponse
{
    [JsonPropertyName("query")]
    public QueryPlanInfo? Query { get; set; }

    [JsonPropertyName("points")]
    public List<SelectorPoint> Points { get; set; } = new();
}

/// <summary>
/// How the selector was resolved and executed. Returned on every query, and it is the
/// whole response when <see cref="SelectorQueryRequest.Explain"/> is true.
/// </summary>
public class QueryPlanInfo
{
    /// <summary>The plan chosen: "Equalities", "Regex", or "Batched".</summary>
    [JsonPropertyName("plan")]
    public string? Plan { get; set; }

    [JsonPropertyName("roundtrips")]
    public int Roundtrips { get; set; }

    /// <summary>How many series the selector resolved to.</summary>
    [JsonPropertyName("seriesRequested")]
    public int SeriesRequested { get; set; }

    [JsonPropertyName("seriesReturned")]
    public int SeriesReturned { get; set; }

    [JsonPropertyName("elapsedMs")]
    public long ElapsedMs { get; set; }

    /// <summary>
    /// Whether these points went through an aggregation window. Check it. Aggregated
    /// timestamps sit on window boundaries, not on the instants the values were measured
    /// at, and nothing else in the payload distinguishes the two.
    /// </summary>
    [JsonPropertyName("aggregated")]
    public bool Aggregated { get; set; }

    /// <summary>The window actually applied, or null when the points are raw. Echoed even
    /// when you supplied it, because the server may have derived it instead.</summary>
    [JsonPropertyName("resampleEvery")]
    public string? ResampleEvery { get; set; }

    /// <summary>The aggregation applied, or "mixed" when signals used their own declared
    /// aggregations. Null when raw.</summary>
    [JsonPropertyName("aggFunction")]
    public string? AggFunction { get; set; }

    /// <summary>Where the window came from: "raw", "explicit", "maxDataPoints" or
    /// "latest".</summary>
    [JsonPropertyName("windowSource")]
    public string? WindowSource { get; set; }
}

public class SelectorPoint
{
    [JsonPropertyName("streamKey")]
    public int StreamKey { get; set; }

    [JsonPropertyName("tag")]
    public string? Tag { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("value")]
    public double Value { get; set; }

    /// <summary>Present only for signals with a decode profile (a bit-field or enum).</summary>
    [JsonPropertyName("decoded")]
    public JsonElement? Decoded { get; set; }
}
