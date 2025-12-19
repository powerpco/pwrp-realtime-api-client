using System.Linq;
using PowerP.Realtime.API.Client;

// Configure credentials via environment variables to avoid committing secrets.
var apiKey = Environment.GetEnvironmentVariable("POWERP_API_KEY");
var baseUrl = Environment.GetEnvironmentVariable("POWERP_API_BASE_URL") ?? "https://tenant.powerp.app/rt-api/api/";

if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException("Set POWERP_API_KEY before running the sample.");
}

var client = new PowerPAPIClient(baseUrl, apiKey);

// Fetch measurements metadata
var measurements = await client.GetMeasurementsAsync();

// Group by database and default aggregation to keep queries consistent.
var groups = measurements
    .GroupBy(row => new { row.DatabaseId, row.DefaultAgg });

// Use small chunks to avoid overwhelming the API. Upper bound is 20.
const int requestedBlockSize = 10;
const int maxBlockSize = 20;
var blockSize = Math.Min(requestedBlockSize, maxBlockSize);
var lookback = TimeSpan.FromMinutes(15); // Raw data must be under 30 minutes.

foreach (var group in groups)
{
    Console.WriteLine($"\nDatabase ID: {group.Key.DatabaseId}, Aggregation: {group.Key.DefaultAgg}");
    var measurementRows = group.ToList();

    var endTime = DateTime.UtcNow;
    var startTime = endTime - lookback;

    for (var start = 0; start < measurementRows.Count; start += blockSize)
    {
        var block = measurementRows.Skip(start).Take(blockSize).ToList();
        var indexes = block.Select(row => row.Index.ToString()).ToList();
        Console.WriteLine($"Processing block {start / blockSize + 1} with {block.Count} measurements");

        var data = await client.GetMeasurementDataAsync(
            group.Key.DatabaseId,
            indexes,
            startTime,
            endTime,
            group.Key.DefaultAgg,
            "200ms");

        if (data.Count == 0)
        {
            Console.WriteLine($"No data received for block {start / blockSize + 1}");
            continue;
        }

        Console.WriteLine($"Received {data.Count} data points for block {start / blockSize + 1}");
        foreach (var item in data)
        {
            Console.WriteLine($"Measurement: {item.Index}, Value: {item.Value}, Timestamp: {item.Timestamp:o}");
        }
    }
}
