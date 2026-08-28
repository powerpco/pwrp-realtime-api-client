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

    /// <summary>
    /// Aggregation window, e.g. "1m". Omit for raw points — the source instants, kept as
    /// they were measured.
    /// </summary>
    [JsonPropertyName("resampleEvery")]
    public string? ResampleEvery { get; set; }

    /// <summary>
    /// Ask for raw points explicitly. Equivalent to omitting <see cref="ResampleEvery"/>,
    /// but it says so out loud, and combining the two is rejected rather than guessed at.
    /// </summary>
    [JsonPropertyName("raw")]
    public bool? Raw { get; set; }

    /// <summary>
    /// How many points you want per series. The server derives the window that fits and
    /// tells you which one it used. Use this instead of guessing a window for a long
    /// range. Ignored when <see cref="ResampleEvery"/> is given.
    /// </summary>
    [JsonPropertyName("maxDataPoints")]
    public int? MaxDataPoints { get; set; }

    /// <summary>
    /// Floor for a window derived from <see cref="MaxDataPoints"/>, e.g. "1s". Set it to
    /// your source's real cadence so the server never advertises resolution the
    /// instrument does not produce.
    /// </summary>
    [JsonPropertyName("minInterval")]
    public string? MinInterval { get; set; }

    /// <summary>
    /// Aggregation to apply, e.g. "mean", "last", "max". Omit and each signal uses the
    /// one declared for it in the catalogue.
    /// </summary>
    [JsonPropertyName("aggFunction")]
    public string? AggFunction { get; set; }

    /// <summary>
    /// When true, the server returns the query plan — how many series the selector
    /// resolves to and how it will run — without executing it. Use it to size a query
    /// before asking for the data.
    /// </summary>
    [JsonPropertyName("explain")]
    public bool Explain { get; set; }

    /// <summary>Expand bit-field / status signals into named conditions alongside the raw value.</summary>
    [JsonPropertyName("decode")]
    public bool Decode { get; set; }
}
