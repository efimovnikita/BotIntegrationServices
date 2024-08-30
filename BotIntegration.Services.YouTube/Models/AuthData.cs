using System.Text.Json.Serialization;

namespace BotIntegration.Services.YouTube.Models;

public class AuthData
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_expires_in")]
    public int RefreshExpiresIn { get; set; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; }

    [JsonPropertyName("not-before-policy")]
    public int NotBeforePolicy { get; set; }

    [JsonPropertyName("scope")]
    public string Scope { get; set; }
    
    [JsonIgnore]
    public DateTime ExpirationTime { get; private set; }

    public void SetExpirationTime()
    {
        ExpirationTime = DateTime.UtcNow.AddSeconds(ExpiresIn);
    }

    public bool IsTokenValid()
    {
        return DateTime.UtcNow < ExpirationTime;
    }

    public bool ShouldRefreshToken(int refreshThresholdSeconds = 30)
    {
        return DateTime.UtcNow.AddSeconds(refreshThresholdSeconds) >= ExpirationTime;
    }
}