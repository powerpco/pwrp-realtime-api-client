
using System.Text.Json.Serialization;

namespace PowerP.Realtime.API.Client.DTO
{
    public class AuthTokenDto
    {
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("tokenType")]
        public string TokenType { get; set; } = string.Empty;

        [JsonPropertyName("expiresIn")]
        public int ExpiresIn { get; set; }
    }
}
