using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        private DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;

        /// <summary>Serialises refreshes so concurrent calls mint one token, not N.</summary>
        private readonly SemaphoreSlim _authGate = new(1, 1);

        /// <summary>
        /// Refresh this long before the token actually expires, so a call that starts just
        /// under the wire does not arrive just over it.
        /// </summary>
        private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

        /// <param name="baseUrl">
        /// Your host's API root, e.g. <c>https://acme.powerp.app/rt-api/api</c>. A trailing
        /// slash is added if missing.
        /// </param>
        /// <param name="handler">
        /// Supply your own handler to set a proxy, pin certificates, or add a retry policy.
        /// Omit it and the client makes its own.
        /// </param>
        /// <param name="timeout">Request timeout; the .NET default of 100 seconds applies otherwise.</param>
        public PowerPAPIClient(string baseUrl, string clientId, string clientSecret,
                               HttpMessageHandler? handler = null, TimeSpan? timeout = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("A base URL is required.", nameof(baseUrl));

            // Uri resolution against a BaseAddress without a trailing slash DROPS the last
            // segment, so ".../rt-api/api" + "v1/auth/token" silently becomes
            // ".../rt-api/v1/auth/token" — every call 401s or 404s and nothing says why.
            // Normalising here means the caller cannot get it wrong.
            _baseUrl = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
            _clientId = clientId;
            _clientSecret = clientSecret;
            _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
            _httpClient.BaseAddress = new Uri(_baseUrl);
            if (timeout is { } t) _httpClient.Timeout = t;
        }

        /// <summary>
        /// Unset optionals are omitted rather than sent as null. A server field that is not
        /// nullable cannot bind a null, so serialising every unset property turned an
        /// ordinary call into a 400 whose detail this client then discarded.
        /// </summary>
        /// <summary>
        /// Throws with the server's own explanation rather than a bare status code.
        /// Refusals carry a stable <c>code</c> and the numbers behind them; discarding the
        /// body left an integrator with "400 Bad Request" and nothing to act on.
        /// </summary>
        private static async Task EnsureSuccessAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;

            var body = await response.Content.ReadAsStringAsync();
            string? code = null, detail = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("code", out var c)) code = c.GetString();
                if (doc.RootElement.TryGetProperty("detail", out var d)) detail = d.GetString();
            }
            catch (JsonException) { /* not a problem document; the raw body is still useful */ }

            var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds
                ?? (response.Headers.RetryAfter?.Date is { } until
                    ? Math.Max(0, (until - DateTimeOffset.UtcNow).TotalSeconds)
                    : (double?)null);

            var explanation = detail ?? (string.IsNullOrWhiteSpace(body) ? "(no body)" : body);
            if (retryAfter is { } wait)
                explanation += $" Retry after {wait:0} seconds.";

            throw new HttpRequestException(
                $"{(int)response.StatusCode} {response.ReasonPhrase}" +
                $"{(code is null ? "" : $" [{code}]")}: {explanation}",
                null, response.StatusCode);
        }

        private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>
        /// Ensures a token that is still valid, minting or refreshing one when it is not.
        ///
        /// The previous version fetched once and cached forever. Tokens last an hour, and a
        /// process that outlives one — a long-running integration, or an MCP server held
        /// open by an AI client for days — started answering 401 to everything and never
        /// recovered. Expiry is what the server already tells us in <c>expires_in</c>.
        /// </summary>
        private async Task EnsureAuthenticatedAsync(bool force = false)
        {
            if (!force && _accessToken is { Length: > 0 }
                && DateTimeOffset.UtcNow < _accessTokenExpiresAt - RefreshMargin)
                return;

            await _authGate.WaitAsync();
            try
            {
                // Re-check inside the gate: several callers may have queued behind one
                // refresh, and only the first needs to do it.
                if (!force && _accessToken is { Length: > 0 }
                    && DateTimeOffset.UtcNow < _accessTokenExpiresAt - RefreshMargin)
                    return;

                var formData = new List<KeyValuePair<string, string>>
                {
                    new("client_id", _clientId),
                    new("client_secret", _clientSecret),
                    new("grant_type", "client_credentials")
                };

                var response = await _httpClient.PostAsync("v1/auth/token", new FormUrlEncodedContent(formData));
                await EnsureSuccessAsync(response);

                var tokenData = await response.Content.ReadFromJsonAsync<AuthTokenDto>()
                    ?? throw new HttpRequestException("The token endpoint returned no token.");

                _accessToken = tokenData.AccessToken;
                // A missing or absurd lifetime is treated as short rather than as forever:
                // refreshing too often costs one request, trusting too long costs an outage.
                var lifetime = tokenData.ExpiresIn > 0
                    ? TimeSpan.FromSeconds(tokenData.ExpiresIn)
                    : TimeSpan.FromMinutes(5);
                _accessTokenExpiresAt = DateTimeOffset.UtcNow + lifetime;

                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

                // Nothing is written to standard output. For a stdio MCP server that stream
                // is the JSON-RPC channel, and a stray line corrupts the protocol.
            }
            finally
            {
                _authGate.Release();
            }
        }

        /// <summary>
        /// Sends a request, and on a 401 mints a fresh token and sends it once more.
        ///
        /// Expiry is handled proactively above; this covers the cases a clock cannot
        /// predict — a signing key rotated under us, or a token invalidated early. One
        /// retry, so a genuinely rejected credential fails as a credential error rather
        /// than looping.
        /// </summary>
        private async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send)
        {
            await EnsureAuthenticatedAsync();

            var response = await send();
            if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized) return response;

            response.Dispose();
            await EnsureAuthenticatedAsync(force: true);
            return await send();
        }

        /// <summary>
        /// The buckets this credential can reach. Tenant-scoped by the server, so it returns
        /// your own and nothing else — the natural first call when you do not yet know an id.
        /// </summary>
        public async Task<IReadOnlyList<DatabaseDto>> GetBucketsAsync()
        {
            var response = await SendAsync(() => _httpClient.GetAsync("v1/databases"));
            await EnsureSuccessAsync(response);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return await response.Content.ReadFromJsonAsync<List<DatabaseDto>>(options) ?? [];
        }

        public async Task<IReadOnlyList<MeasurementDto>> GetMeasurementsAsync()
        {
            var response = await SendAsync(() => _httpClient.GetAsync("v1/measurements"));
            await EnsureSuccessAsync(response);
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

            // Re-reading previous `PowerPAPIClient.cs`: it was `_httpClient.PostAsJsonAsync("Query", payload);`
            // QueryController maps to api/v1/Query (controller name).
            
            var response = await SendAsync(() => _httpClient.PostAsJsonAsync("v1/Query", payload));
            await EnsureSuccessAsync(response);

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
        /// <param name="resampleEvery">Aggregation window (e.g. "1m"); null for raw points.</param>
        /// <param name="maxDataPoints">Points wanted per series; the server derives the
        /// window. Ignored when <paramref name="resampleEvery"/> is given.</param>
        /// <param name="minInterval">Floor for a derived window, e.g. "1s".</param>
        /// <param name="aggFunction">Aggregation to apply; null uses each signal's own.</param>
        /// <param name="streamKeys">Pin an exact signal set by key; intersects with
        /// <paramref name="selector"/>. Prefer this for a scheduled ingest, where a
        /// re-tagged signal must not silently change what you collect.</param>
        /// <param name="decode">Expand status and bit-field signals into named conditions.</param>
        /// <param name="explain">When true, returns the plan only, without executing it.</param>
        /// <remarks>
        /// With neither <paramref name="resampleEvery"/> nor <paramref name="maxDataPoints"/>
        /// you get raw points. A range too wide for that is refused with 400 rather than
        /// quietly aggregated, so check <see cref="QueryPlanInfo.Aggregated"/> on the way
        /// back rather than assuming.
        /// </remarks>
        public async Task<SelectorQueryResponse> QuerySelectorAsync(
            int databaseId,
            Dictionary<string, string> selector,
            DateTime startTime,
            DateTime endTime,
            string? resampleEvery = null,
            int? maxDataPoints = null,
            string? minInterval = null,
            string? aggFunction = null,
            IEnumerable<int>? streamKeys = null,
            bool decode = false,
            bool explain = false)
        {
            var payload = new SelectorQueryRequest
            {
                DatabaseId = databaseId,
                Selector = selector ?? new Dictionary<string, string>(),
                StreamKeys = streamKeys?.ToList(),
                Decode = decode,
                StartTime = startTime,
                EndTime = endTime,
                ResampleEvery = resampleEvery,
                MaxDataPoints = maxDataPoints,
                MinInterval = minInterval,
                AggFunction = aggFunction,
                Explain = explain
            };

            var response = await SendAsync(() => _httpClient.PostAsJsonAsync("v2/query", payload, RequestJson));
            await EnsureSuccessAsync(response);

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
            var payload = new SelectorQueryRequest
            {
                DatabaseId = databaseId,
                Selector = selector ?? new Dictionary<string, string>(),
                Decode = decode,
            };

            var response = await SendAsync(() => _httpClient.PostAsJsonAsync("v2/query/latest", payload, RequestJson));
            await EnsureSuccessAsync(response);

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
            var response = await SendAsync(() => _httpClient.GetAsync($"v2/databases/{databaseId}/vocabulary"));
            await EnsureSuccessAsync(response);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = await response.Content.ReadFromJsonAsync<VocabularyResponse>(options);
            return data ?? new VocabularyResponse();
        }
    }
}
