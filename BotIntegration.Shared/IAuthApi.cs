using Refit;

namespace BotIntegration.Shared;

public interface IAuthApi
{
    [Post("/token")]
    Task<AuthData> GetAuthData([Body(BodySerializationMethod.UrlEncoded)] Dictionary<string, string> data);

    public const string GrantType = "grant_type";
    public const string ClientCredentials = "client_credentials";
    public const string ClientId = "client_id";
    public const string ClientSecret = "client_secret";
}