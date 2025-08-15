# PowerP API Client: Technical Documentation

**Date**: August 14, 2025

## Overview

This document provides detailed technical documentation for a C# sample application that interacts with the PowerP Real-Time API. The application, implemented using the `PowerPAPIClient` class, retrieves measurement metadata and processes measurement data in blocks, grouped by database ID and aggregation function. The API is accessed via HTTPS requests to `https://{tenant}.powerp.app/rt-api/api/`, authenticated with a Bearer token.

The sample code demonstrates how to:
- Initialize the API client with a base URL and API key.
- Fetch measurement metadata.
- Group measurements by database ID and default aggregation function.
- Process measurements in blocks with a specified lookback period.
- Retrieve and display measurement data.

## API Description

The PowerP Real-Time API provides access to power plant measurement data. The relevant endpoints used in the sample are:

### GET /rt-api/api/measurements
- **Description**: Retrieves metadata for all measurements, including ID, database ID, name, description, tag, index, data type, min/max values, unit symbol, first data point, and default aggregation function.
- **Response**: JSON array of measurement objects.
- **Headers**:
  - `Content-Type: application/json`
  - `Authorization: Bearer <token>`
- **Expected Status**: 200 OK

### POST /rt-api/api/Query
- **Description**: Queries measurement data for a specific database, measurement indexes, time range, and aggregation function.
- **Request Body**:
  ```json
  {
    "databaseId": integer,
    "measurementIndexes": string[],
    "startTime": string (ISO 8601, e.g., "2025-08-14T15:15:00Z"),
    "endTime": string (ISO 8601),
    "aggFunction": string (e.g., "last", "mean", "max"),
    "windowPeriod": string (e.g., "200ms")
  }
  ```
- **Response**: JSON array of measurement data points, each containing index, value, and timestamp.
- **Headers**: Same as above.
- **Expected Status**: 200 OK

## Code Structure

The sample code is a C# console application that uses the `PowerPAPIClient` class to interact with the API. Below is the complete code with inline comments for clarity:

```csharp
using PowerP.Realtime.API.Client;

string _apiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...";
string _baseUrl = "https://xxx.powerp.app/rt-api/api/";

// Initializing the API client with base URL and API key
var client = new PowerPAPIClient(_baseUrl, _apiKey);

// Fetching measurements metadata
var m = await client.GetMeasurementsAsync();

// Grouping measurements by database ID and default aggregation function
var groups = m.AsEnumerable().GroupBy(row => new
{
    DatabaseId = row.DatabaseId,
    AggFunction = row.DefaultAgg
});

foreach (var group in groups)
{
    Console.WriteLine($"\nProcessing Database ID: {group.Key.DatabaseId}, Aggregation: {group.Key.AggFunction}");
    
    var measurementRows = group.ToList();

    // Setting maximum block size to 20, using 5 for this example
    var blockSize = 5;

    // Setting lookback period to 15 minutes for raw data
    var lookbackMinutes = 15;

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
```

## Code Explanation

- **Initialization**: The `PowerPAPIClient` is instantiated with the API base URL and a Bearer token for authentication.
- **Fetching Metadata**: The `GetMeasurementsAsync` method retrieves measurement metadata, returning a collection of `Measurement` objects with properties like `DatabaseId`, `Name`, `Description`, `Tag`, `Index`, `DataType`, `MinValue`, `MaxValue`, `UnitSymbol`, `FirstDataPoint` and `DefaultAgg`.
- **Grouping**: Measurements are grouped by `DatabaseId` and `DefaultAgg` using LINQ to ensure data is processed consistently for each database and aggregation function.
- **Block Processing**: Measurements are processed in blocks of up to 5 (configurable, max 20) to respect API limits. The lookback period is set to 15 minutes to comply with the API's raw data constraint of less than 30 minutes.
- **Data Retrieval**: The `GetMeasurementDataAsync` method queries the API for measurement data, passing the database ID, measurement indexes, time range, aggregation function, and a 200ms window period.
- **Output**: Retrieved data points are printed to the console, showing the measurement index, value, and timestamp.

## Usage Instructions

To use this sample code:

1. **Prerequisites**: Ensure you have .NET (e.g., .NET 6.0 or later) installed and the `PowerP.Realtime.API.Client` library available.
2. **Setup**: Replace the `_apiKey` with a valid Bearer token for the Power Plant API.
3. **Configuration**:
   - Set `blockSize` (default 5, max 20) to control the number of measurements per API call.
   - Set `lookbackMinutes` (default 15, max 30) to define the time range for raw data.
4. **Run**: Execute the console application. It will fetch measurement metadata, group it, and process data in blocks, printing results to the console.

## Assumptions

- The `PowerPAPIClient` class implements `GetMeasurementsAsync` and `GetMeasurementDataAsync`, returning a collection of measurement objects and data points, respectively.
- The measurement objects have properties like `DatabaseId`, `Index`, and `DefaultAgg`.
- The API enforces a maximum block size of 20 measurements and a 30-minute limit for raw data queries.

## Limitations

- The sample does not handle rate limiting or retry logic for API failures.
- Data is output to the console; additional logic is needed.
- The `windowPeriod` is hardcoded to "200ms"; this may need adjustment based on API requirements.

## Conclusion

This C# sample application demonstrates how to interact with the PowerP Real-Time API to fetch and process measurement data efficiently. By grouping measurements and processing them in blocks, it respects API constraints while providing a scalable approach to data retrieval.