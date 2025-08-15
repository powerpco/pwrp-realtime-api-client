using System.Text.Json.Serialization;

namespace PowerP.Realtime.API.Client.DTO;

public class MeasurementValueDto
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("value")]
    public double Value { get; set; }
}