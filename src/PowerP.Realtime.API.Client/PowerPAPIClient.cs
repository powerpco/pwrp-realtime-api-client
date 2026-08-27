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
            
            var formData = new List<KeyValuePair<string, string>>
            {
                new("client_id", _clientId),
                new("client_secret", _clientSecret),
                new("grant_type", "client_credentials")
            };
            
            // Note: Adjust path if API prefixes change. Assuming base includes /api or logic handles it.
            // Based on other files, auth is at /api/v1/auth/token
            // If base url is http://locahost:5000/api, then path is v1/auth/token

            var response = await _httpClient.PostAsync("v1/auth/token", new FormUrlEncodedContent(formData));
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

        /// <summary>
        /// v2 selector query. Describe what you want by its semantic tags and the server
        /// resolves it to series and runs the cheapest plan — no need to enumerate
        /// indexes or stay under the 20-signal block size the v1 path requires.
        /// </summary>
        /// <param name="selector">Semantic tags, e.g. { "site": "SITE01", "signal": "active_power" }.</param>
        /// <param name="resampleEvery">Optional aggregation window (e.g. "1m"); null for raw points.</param>
        /// <param name="explain">When true, returns the plan only, without executing it.</param>
        public async Task<SelectorQueryResponse> QuerySelectorAsync(
            int databaseId,
            Dictionary<string, string> selector,
            DateTime startTime,
            DateTime endTime,
            string? resampleEvery = null,
            bool explain = false)
        {
            await EnsureAuthenticatedAsync();

            var payload = new SelectorQueryRequest
            {
                DatabaseId = databaseId,
                Selector = selector ?? new Dictionary<string, string>(),
                StartTime = startTime,
                EndTime = endTime,
                ResampleEvery = resampleEvery,
                Explain = explain
            };

            var response = await _httpClient.PostAsJsonAsync("v2/query", payload);
            response.EnsureSuccessStatusCode();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = await response.Content.ReadFromJsonAsync<SelectorQueryResponse>(options);
            return data ?? new SelectorQueryResponse();
        }

        /// <summary>
        /// The latest value for every series a selector resolves to — the polling pattern
        /// in one call. No window: it reads the last point per series.
        /// </summary>
        public async Task<SelectorQueryResponse> QuerySelectorLatestAsync(
            int databaseId, Dictionary<string, string> selector, bool decode = false)
        {
            await EnsureAuthenticatedAsync();

            var payload = new SelectorQueryRequest
            {
                DatabaseId = databaseId,
                Selector = selector ?? new Dictionary<string, string>(),
                Decode = decode,
            };

            var response = await _httpClient.PostAsJsonAsync("v2/query/latest", payload);
            response.EnsureSuccessStatusCode();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = await response.Content.ReadFromJsonAsync<SelectorQueryResponse>(options);
            return data ?? new SelectorQueryResponse();
        }

        /// <summary>
        /// The selector vocabulary of a bucket: the tag dimensions and their values. Call
        /// it first to discover what a bucket can be queried by, then build selectors.
        /// </summary>
        public async Task<VocabularyResponse> GetVocabularyAsync(int databaseId)
        {
            await EnsureAuthenticatedAsync();

            var response = await _httpClient.GetAsync($"v2/databases/{databaseId}/vocabulary");
            response.EnsureSuccessStatusCode();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = await response.Content.ReadFromJsonAsync<VocabularyResponse>(options);
            return data ?? new VocabularyResponse();
        }
    }
}
