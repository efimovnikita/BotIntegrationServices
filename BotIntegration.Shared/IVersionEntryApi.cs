using Refit;

namespace BotIntegration.Shared;

public interface IVersionEntryApi
{
    [Multipart]
    [Post("/api/gateway/artifacts-repo/v1/version-entry")]
    Task<CurrentVersionResponse> CreateVersionEntry(
        [Header("Authorization")] string authorization,
        [AliasAs("AppName")] string appName,
        [AliasAs("MajorVersion")] int majorVersion,
        [AliasAs("MinorVersion")] int minorVersion,
        [AliasAs("PatchVersion")] int patchVersion,
        [AliasAs("Date")] string date, // Change DateTime to string
        [AliasAs("Notes")] string notes,
        [AliasAs("artifactsZip")] StreamPart artifactsZip
    );

    [Get("/api/gateway/artifacts-repo/v1/version-entry/all")]
    Task<string> GetAllVersionEntries([Header("Authorization")] string authorization);
    
    [Get("/api/gateway/artifacts-repo/v1/version-entry/{appName}/latest/archive")]
    Task<Stream> GetLatestVersionArchive([Header("Authorization")] string authorization, string appName);

    [Get("/api/gateway/artifacts-repo/v1/version-entry/{appName}/current-version")]
    Task<CurrentVersionResponse> GetCurrentVersion([Header("Authorization")] string authorization, string appName);
}