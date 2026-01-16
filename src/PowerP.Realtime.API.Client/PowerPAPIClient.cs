using System.Net.Http.Json;
using System.Text.Json;
using PowerP.Realtime.API.Client.DTO;

namespace PowerP.Realtime.API.Client
{
    public class PowerPAPIClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private string? _accessToken;

        public PowerPAPIClient(string baseUrl, string clientId, string clientSecret)
        {
            _baseUrl = baseUrl;
            _clientId = clientId;
            _clientSecret = clientSecret;
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(_baseUrl)
            };
        }

        private async Task EnsureAuthenticatedAsync()
        {
            if (!string.IsNullOrEmpty(_accessToken)) return;

            var authRequest = new { clientId = _clientId, clientSecret = _clientSecret };
            // Note: Adjust path if API prefixes change. Assuming base includes /api or logic handles it.
            // Based on other files, auth is at /api/v1/auth/token
            // If base url is http://locahost:5000/api, then path is v1/auth/token
            
            var response = await _httpClient.PostAsJsonAsync("v1/auth/token", authRequest);
            response.EnsureSuccessStatusCode();

            var tokenData = await response.Content.ReadFromJsonAsync<AuthTokenDto>();
            if (tokenData != null)
            {
                _accessToken = tokenData.AccessToken;
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
                Console.WriteLine($"[Client] Successfully Authenticated. Token Length: {_accessToken.Length}");
            }
        }

        public async Task<IReadOnlyList<MeasurementDto>> GetMeasurementsAsync()
        {
            await EnsureAuthenticatedAsync();
            var response = await _httpClient.GetAsync("v1/measurements"); 
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
            await EnsureAuthenticatedAsync();

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

            // Re-reading previous `PowerPAPIClient.cs`: it was `_httpClient.PostAsJsonAsync("Query", payload);`
            // QueryController maps to api/v1/Query (controller name).
            
            var response = await _httpClient.PostAsJsonAsync("v1/Query", payload);
            response.EnsureSuccessStatusCode();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = await response.Content.ReadFromJsonAsync<List<MeasurementValueDto>>(options);
            return data ?? new List<MeasurementValueDto>();
        }
    }
}
