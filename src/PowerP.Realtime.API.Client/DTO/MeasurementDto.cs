using System;
using System.Text.Json.Serialization;

namespace PowerP.Realtime.API.Client.DTO;

public class MeasurementDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("databaseId")]
    public int DatabaseId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("tag")]
    public string Tag { get; set; } = string.Empty;

    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("dataType")]
    public string DataType { get; set; } = string.Empty;

    [JsonPropertyName("minValue")]
    public double MinValue { get; set; }

    [JsonPropertyName("maxValue")]
    public double MaxValue { get; set; }

    [JsonPropertyName("unitSymbol")]
    public string UnitSymbol { get; set; } = string.Empty;

    [JsonPropertyName("firstDataPoint")]
    public DateTime FirstDataPoint { get; set; }

    [JsonPropertyName("defaultAgg")]
    public string DefaultAgg { get; set; } = string.Empty;
}