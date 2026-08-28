using PowerP.Realtime.API.Client;

// Credentials come from the environment so nothing is committed.
var clientId = Environment.GetEnvironmentVariable("POWERP_CLIENT_ID");
var clientSecret = Environment.GetEnvironmentVariable("POWERP_CLIENT_SECRET");
var baseUrl = Environment.GetEnvironmentVariable("POWERP_API_BASE_URL") ?? "http://localhost:5000/api/";

if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
    throw new InvalidOperationException("Set POWERP_CLIENT_ID and POWERP_CLIENT_SECRET before running.");

var databaseId = int.TryParse(Environment.GetEnvironmentVariable("POWERP_DATABASE_ID"), out var db) ? db : 1;

// Reuse one client for the process lifetime (it caches the token and the HttpClient).
var client = new PowerPAPIClient(baseUrl, clientId, clientSecret);

// 1. Discover: what can this bucket be queried by?
var vocab = await client.GetVocabularyAsync(databaseId);
Console.WriteLine($"Bucket '{vocab.Bucket}' — {vocab.Signals} signals");
foreach (var (dimension, values) in vocab.Dimensions)
    Console.WriteLine($"  {dimension}: {string.Join(", ", values.Take(8))}");

// Build a selector from the vocabulary. This is a placeholder — adjust to your bucket.
var selector = new Dictionary<string, string> { ["level"] = "inverter", ["signal"] = "active_power" };

// 2. Size it first: explain returns the plan without moving data.
var plan = await client.QuerySelectorAsync(
    databaseId, selector, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, explain: true);
Console.WriteLine($"\nplan={plan.Query?.Plan} series={plan.Query?.SeriesRequested} " +
                  $"roundtrips={plan.Query?.Roundtrips}");

// 3. Latest value per series — the polling pattern, one call.
var latest = await client.QuerySelectorLatestAsync(databaseId, selector);
Console.WriteLine($"\nlatest: {latest.Points.Count} series");
foreach (var p in latest.Points.Take(8))
    Console.WriteLine($"  {p.Tag} = {p.Value} @ {p.Timestamp:o}");

// 4. Range query, resampled to 5-minute windows.
var series = await client.QuerySelectorAsync(
    databaseId, selector, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, resampleEvery: "5m");
Console.WriteLine($"\nrange: {series.Points.Count} points "
    + $"(aggregated={series.Query?.Aggregated} every={series.Query?.ResampleEvery})");

// 5. Raw points: no window, so every timestamp is the instant the value was recorded.
//    Use this for states, alarms and setpoints, where a value moved to a window boundary
//    is reported earlier than it actually happened. Raw is bounded: too wide a range or
//    too many signals is refused with 400 rather than quietly aggregated.
var raw = await client.QuerySelectorAsync(
    databaseId, selector, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow);
Console.WriteLine($"raw: {raw.Points.Count} points (aggregated={raw.Query?.Aggregated})");

// 6. Or state a point budget and let the server choose the window.
var budgeted = await client.QuerySelectorAsync(
    databaseId, selector, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, maxDataPoints: 200);
Console.WriteLine($"budgeted: {budgeted.Points.Count} points "
    + $"at every={budgeted.Query?.ResampleEvery}");
