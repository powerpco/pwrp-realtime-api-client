using System.Text.Json.Serialization;

namespace PowerP.Realtime.API.Client.DTO;

/// <summary>
/// The selector vocabulary of a bucket: every tag dimension and the values it takes.
/// Call it first to learn what selectors a bucket supports, then build queries from it.
/// </summary>
public class VocabularyResponse
{
    [JsonPropertyName("databaseId")]
    public int DatabaseId { get; set; }

    [JsonPropertyName("bucket")]
    public string? Bucket { get; set; }

    [JsonPropertyName("signals")]
    public int Signals { get; set; }

    /// <summary>Dimension name to its values, e.g. "level" -> ["inverter", "meter", "relay"].</summary>
    [JsonPropertyName("dimensions")]
    public Dictionary<string, List<string>> Dimensions { get; set; } = new();
}
