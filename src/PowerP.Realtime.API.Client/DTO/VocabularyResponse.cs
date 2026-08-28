using System.Text.Json.Serialization;

namespace PowerP.Realtime.API.Client.DTO;

/// <summary>
/// The selector vocabulary of a bucket: every tag dimension and the values it takes.
/// Call it first to learn what selectors a bucket supports, then build queries from it.
/// </summary>
public class VocabularyResponse
{
    /// <summary>The bucket. v2 renamed database to bucket; both spellings are read so the
    /// client keeps working across the rename.</summary>
    [JsonPropertyName("bucketId")]
    public int BucketId { get; set; }

    /// <summary>Deprecated spelling of <see cref="BucketId"/>.</summary>
    [JsonPropertyName("databaseId")]
    public int DatabaseId { get; set; }

    /// <summary>Whichever the server sent.</summary>
    [JsonIgnore]
    public int Id => BucketId > 0 ? BucketId : DatabaseId;

    [JsonPropertyName("bucket")]
    public string? Bucket { get; set; }

    [JsonPropertyName("signals")]
    public int Signals { get; set; }

    /// <summary>Dimension name to its values, e.g. "level" -> ["inverter", "meter", "relay"].</summary>
    [JsonPropertyName("dimensions")]
    public Dictionary<string, List<string>> Dimensions { get; set; } = new();
}
