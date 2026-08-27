using System.Linq;
using PowerP.Realtime.API.Client;

// Configure credentials via environment variables to avoid committing secrets.
var clientId = Environment.GetEnvironmentVariable("POWERP_CLIENT_ID");
var clientSecret = Environment.GetEnvironmentVariable("POWERP_CLIENT_SECRET");
var baseUrl = Environment.GetEnvironmentVariable("POWERP_API_BASE_URL") ?? "http://localhost:5000/api/";

if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
{
    throw new InvalidOperationException("Set POWERP_CLIENT_ID and POWERP_CLIENT_SECRET before running the sample.");
}

var client = new PowerPAPIClient(baseUrl, clientId, clientSecret);

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

// ---------------------------------------------------------------------------
// v2 selector query: describe what you want by its tags, get a whole site in one
// request. No 20-signal block size to work around. Replace the selector with your
// bucket's vocabulary (ask the PowerP team) and databaseId.
// ---------------------------------------------------------------------------
Console.WriteLine("\n--- v2 selector query ---");
var selector = new Dictionary<string, string>
{
    ["site"] = "SITE01",
    ["level"] = "inverter",
    ["signal"] = "active_power"
};
var v2End = DateTime.UtcNow;
var v2Start = v2End - TimeSpan.FromHours(1);

// Size it first: explain returns the plan without moving any data.
var planned = await client.QuerySelectorAsync(databaseId: 1, selector, v2Start, v2End, explain: true);
Console.WriteLine($"plan={planned.Query?.Plan} series={planned.Query?.SeriesRequested} " +
                  $"roundtrips={planned.Query?.Roundtrips} elapsedMs={planned.Query?.ElapsedMs}");

// Then run it.
var v2 = await client.QuerySelectorAsync(databaseId: 1, selector, v2Start, v2End);
Console.WriteLine($"Received {v2.Points.Count} points");
foreach (var p in v2.Points.Take(5))
{
    Console.WriteLine($"  {p.Tag}  {p.Value}  {p.Timestamp:o}");
}
