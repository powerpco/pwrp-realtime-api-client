using System.Net.Http.Json;
using System.Text.Json;
using PowerP.Realtime.API.Client.DTO;

namespace PowerP.Realtime.API.Client
{
    public class PowerPAPIClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _apiKey;

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

        public async Task<IReadOnlyList<MeasurementDto>> GetMeasurementsAsync()
        {
            var response = await _httpClient.GetAsync("measurements");
            response.EnsureSuccessStatusCode();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var measurements = await response.Content.ReadFromJsonAsync<List<MeasurementDto>>(options);
            return measurements ?? new List<MeasurementDto>();
        }

        public async Task<IReadOnlyList<MeasurementValueDto>> GetMeasurementDataAsync(
            int databaseId,
            List<string> measurementIndexes,
            DateTime startTime,
            DateTime endTime,
            string aggFunction,
            string windowPeriod = "200ms")
        {
            if (measurementIndexes == null || measurementIndexes.Count == 0)
            {
                return Array.Empty<MeasurementValueDto>();
            }

            var payload = new QueryRequest
            {
                DatabaseId = databaseId,
                MeasurementIndexes = measurementIndexes,
                StartTime = startTime,
                EndTime = endTime,
                AggFunction = aggFunction,
                WindowPeriod = windowPeriod
            };

            var response = await _httpClient.PostAsJsonAsync("Query", payload);
            response.EnsureSuccessStatusCode();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = await response.Content.ReadFromJsonAsync<List<MeasurementValueDto>>(options);
            return data ?? new List<MeasurementValueDto>();
        }
    }
}
