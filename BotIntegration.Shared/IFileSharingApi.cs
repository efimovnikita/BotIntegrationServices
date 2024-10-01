using Refit;

namespace BotIntegration.Shared;

public interface IFileSharingApi
{
    [Get("/api/gateway/files-share/v1/health")]
    Task CheckHealth();
    
    [Multipart]
    [Post("/api/gateway/files-share/v1/upload")]
    Task<UploadData> UploadFile([Header("Authorization")] string authorization,
        [AliasAs("file")] StreamPart file);
}