using System.Globalization;
using System.Text;
using System.Text.Json;
using PowerP.Realtime.API.Client.DTO;

namespace PowerP.Realtime.Cli;

public enum Format { Table, Json, Csv }

/// <summary>
/// Rendering. Three formats because the three uses are different: a person reading a
/// terminal, a script piping JSON, and a spreadsheet.
/// </summary>
public static class Output
{
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void Write(object value, Format format)
    {
        if (format == Format.Json) { Console.WriteLine(JsonSerializer.Serialize(value, Pretty)); return; }
        throw new InvalidOperationException("Table and CSV are rendered per command.");
    }

    public static void Json(object value) => Console.WriteLine(JsonSerializer.Serialize(value, Pretty));

    /// <summary>
    /// Points, with the header a reader needs before trusting a timestamp.
    ///
    /// The plan line is not decoration: aggregated points sit on window boundaries, and a
    /// column of times gives no hint which kind it is holding.
    /// </summary>
    public static void Points(SelectorQueryResponse r, Format format)
    {
        switch (format)
        {
            case Format.Json:
                Json(r);
                return;

            case Format.Csv:
                var csv = new StringBuilder("streamKey,tag,timestamp,value\n");
                foreach (var p in r.Points)
                    csv.Append(p.StreamKey).Append(',')
                       .Append(Escape(p.Tag)).Append(',')
                       .Append(p.Timestamp.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                       .Append(p.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
                Console.Write(csv.ToString());
                return;

            default:
                var q = r.Query;
                var kind = q?.Aggregated == true
                    ? $"aggregated every {q.ResampleEvery} with {q.AggFunction} ({q.WindowSource})"
                    : "raw — timestamps are the instants the values were recorded at";
                Console.Error.WriteLine($"# {r.Points.Count} points, {q?.SeriesReturned ?? 0} series, {kind}");
                if (q?.EstimatedPoints is { } est)
                    Console.Error.WriteLine($"# estimated {est:N0} points");
                if (q?.UnresolvedStreamKeys is { Count: > 0 } missing)
                    Console.Error.WriteLine($"# unresolved stream keys: {string.Join(", ", missing)}");

                Console.WriteLine($"{"STREAM KEY",-12} {"TAG",-38} {"TIMESTAMP",-26} VALUE");
                foreach (var p in r.Points)
                    Console.WriteLine($"{p.StreamKey,-12} {Truncate(p.Tag, 38),-38} " +
                                      $"{p.Timestamp:yyyy-MM-dd HH:mm:ss.fff}    {p.Value,12:G6}");
                return;
        }
    }

    private static string Escape(string? s) =>
        s is null ? "" : s.Contains(',') || s.Contains('"')
            ? $"\"{s.Replace("\"", "\"\"")}\""
            : s;

    private static string Truncate(string? s, int width) =>
        s is null ? "" : s.Length <= width ? s : s[..(width - 1)] + "…";
}
