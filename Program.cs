// See https://aka.ms/new-console-template for more information
using PowerP.Realtime.API.Client;

string _apiKey = "XXXXXXXXXXXXXXXXX"; // Replace with your actual API key
string _baseUrl = "https://tenant.powerp.app/rt-api/api/"; // Replace with your actual base URL

var client = new PowerPAPIClient(_baseUrl, _apiKey);

// Fetch measurements metadata
var m = await client.GetMeasurementsAsync();

// Measurements can be in different databases and every measurement can have a different default aggregation function.
// We will group them by database ID and default aggregation function.
var groups = m.AsEnumerable().GroupBy(row => new
    {
        DatabaseId = row.DatabaseId,
        AggFunction = row.DefaultAgg
    });

foreach (var group in groups)
{
    Console.WriteLine($"\nProcessing Database ID: {group.Key.DatabaseId}, Aggregation: {group.Key.AggFunction}");
    
    var measurementRows = group.ToList();

    //Maximum number of measurements to process in each block is 20
    var blockSize = 5; // Number of measurements to process in each block

    // For raw data need to be less than 30 minutes
    var lookbackMinutes = 15; // Lookback period in minutes

    var endTime = DateTime.UtcNow;
    var startTime = endTime.AddMinutes(-lookbackMinutes);

    for (int start = 0; start < measurementRows.Count; start += blockSize)
    {
        var block = measurementRows.Skip(start).Take(blockSize).ToList();
        Console.WriteLine($"Processing block {start / blockSize + 1} with {block.Count} measurements");

        var indexes = block.Select(row => row.Index.ToString()).ToList();

        var data = await client.GetMeasurementDataAsync(
            group.Key.DatabaseId,
            indexes,
            startTime,
            endTime,
            group.Key.AggFunction,
            "200ms"
        );

        if (data != null && data.Count > 0)
        {
            Console.WriteLine($"Received {data.Count} data points for block {start / blockSize + 1}");
            foreach (var item in data)
            {
                Console.WriteLine($"Measurement: {item.Index}, Value: {item.Value}, Timestamp: {item.Timestamp}");
            }
        }
        else
        {
            Console.WriteLine($"No data received for block {start / blockSize + 1}");
        }
    }
}