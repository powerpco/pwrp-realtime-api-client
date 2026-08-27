using System.Text.Json.Serialization;

namespace PowerP.Realtime.API.Client.DTO;

/// <summary>
/// A v2 query. Instead of listing measurement indexes, you describe what you want by its
/// semantic tags and the server resolves it to series and compiles the cheapest plan.
/// A whole site of thousands of signals is a single request.
/// </summary>
public class SelectorQueryRequest
{
    [JsonPropertyName("databaseId")]
    public int DatabaseId { get; set; }

    /// <summary>
    /// Semantic selector, e.g. { "site": "SITE01", "level": "inverter",
    /// "signal": "active_power" }. Keys are the tag dimensions the catalogue exposes;
    /// an empty selector matches the whole bucket.
    /// </summary>
    [JsonPropertyName("selector")]
    public Dictionary<string, string> Selector { get; set; } = new();

    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public DateTime EndTime { get; set; }

    /// <summary>Optional per-signal aggregation window (e.g. "1m"). Omit for raw points.</summary>
    [JsonPropertyName("resampleEvery")]
    public string? ResampleEvery { get; set; }

    /// <summary>
    /// When true, the server returns the query plan — how many series the selector
    /// resolves to and how it will run — without executing it. Use it to size a query
    /// before asking for the data.
    /// </summary>
    [JsonPropertyName("explain")]
    public bool Explain { get; set; }
}
