using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using System.Text.Json;
using PowerP.Realtime.API.Client.DTO;

namespace PowerP.Realtime.API.Client
{
    public class PowerPAPIClient
    {
        private readonly HttpClient _httpClient;
        private string _baseUrl;
        private string _apiKey;

        public PowerPAPIClient(string baseUrl, string apiKey)
        {
            _baseUrl = baseUrl;
            _apiKey = apiKey;
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(_baseUrl)
            };
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        }

        public async Task<List<MeasurementDto>> GetMeasurementsAsync(bool refresh = false)
        {
            var response = await _httpClient.GetAsync("measurements");
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Error fetching measurements: {response.ReasonPhrase}");
            }

            var content = await response.Content.ReadAsStringAsync();

            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true // Optional if using JsonPropertyName attributes
                };
                var measurements = JsonSerializer.Deserialize<List<DTO.MeasurementDto>>(content, options);
                return measurements;
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Deserialization failed: {ex.Message}");
                return null;
            }
        }

       public async Task<List<MeasurementValueDto>> GetMeasurementDataAsync(
            int databaseId,
            List<string> measurementIndexes,
            DateTime startTime,
            DateTime endTime,
            string aggFunction,
            string windowPeriod = "200ms")
        {
            var payload = new QueryRequest
            {
                DatabaseId = databaseId,
                MeasurementIndexes = measurementIndexes,
                StartTime = startTime, //.ToString("O") + "Z",
                EndTime =endTime, //.ToString("O") + "Z",
                AggFunction =aggFunction,
                WindowPeriod = windowPeriod
            };

            Console.WriteLine($"Payload for measurement data: {JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true })}");

            try
            {
                var response = await _httpClient.PostAsJsonAsync(_baseUrl + "Query", payload);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<List<MeasurementValueDto>>(json);
                return data;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Error fetching measurement data: {ex.Message}");
                return null;
            }
        }
    }
}