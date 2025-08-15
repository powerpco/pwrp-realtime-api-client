using System.Text.Json.Serialization;

namespace PowerP.Realtime.API.Client.DTO;

public class QueryRequest
{
    public int DatabaseId { get; set; }

    public List<string> MeasurementIndexes { get; set; } = new();
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? AggFunction { get; set; }
    public string? WindowPeriod { get; set; }
}