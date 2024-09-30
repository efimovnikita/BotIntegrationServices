using BotIntegration.Services.ArtifactsRepo.Models;
using BotIntegration.Services.ArtifactsRepo.Services;
using BotIntegration.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BotIntegration.Services.ArtifactsRepo.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VersionEntryController(DatabaseService databaseService, ILogger<VersionEntryController> logger)
    : ControllerBase
{
    [HttpPost]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue, ValueLengthLimit = int.MaxValue)]
    public async Task<IActionResult> CreateVersionEntry([FromForm] VersionEntryRequest request, IFormFile artifactsZip)
    {
        if (!ModelState.IsValid)
        {
            logger.LogWarning("Invalid model state for CreateVersionEntry request");
            return BadRequest(ModelState);
        }

        try
        {
            logger.LogInformation("Creating version entry for app: {AppName}, version: {MajorVersion}.{MinorVersion}.{PatchVersion}", 
                request.AppName, request.MajorVersion, request.MinorVersion, request.PatchVersion);

            // Check if the version already exists
            var existingVersion = await databaseService.GetVersionEntryAsync(request.AppName, request.MajorVersion, request.MinorVersion, request.PatchVersion);
            if (existingVersion != null)
            {
                logger.LogWarning("Version {MajorVersion}.{MinorVersion}.{PatchVersion} already exists for app {AppName}", 
                    request.MajorVersion, request.MinorVersion, request.PatchVersion, request.AppName);
                return Conflict($"Version {request.MajorVersion}.{request.MinorVersion}.{request.PatchVersion} already exists for app {request.AppName}");
            }

            // Store the zip archive
            string tempFilePath = Path.GetTempFileName();
            logger.LogInformation("Temporary file created at: {TempFilePath}", tempFilePath);

            using (var fileStream = new FileStream(tempFilePath, FileMode.Create))
            {
                await artifactsZip.CopyToAsync(fileStream);
                logger.LogInformation("Copied artifacts zip to temporary file.");
            }

            string archiveId;
            using (var fileStream = new FileStream(tempFilePath, FileMode.Open))
            {
                archiveId = await databaseService.StoreZipArchiveAsync(artifactsZip.FileName, fileStream);
                logger.LogInformation("Stored zip archive with ID: {ArchiveId}", archiveId);
            }

            System.IO.File.Delete(tempFilePath);
            logger.LogInformation("Deleted temporary file at: {TempFilePath}", tempFilePath);

            // Create the version entry
            var versionEntry = new AppArtifactsVersionEntry
            {
                AppName = request.AppName,
                MajorVersion = request.MajorVersion,
                MinorVersion = request.MinorVersion,
                PatchVersion = request.PatchVersion,
                Date = request.Date,
                Notes = request.Notes,
                ArtifactsArchiveId = archiveId
            };

            var createdEntry = await databaseService.CreateVersionEntryAsync(versionEntry);

            logger.LogInformation("Created version entry with ID: {EntryId}", createdEntry.EntryId);

            return CreatedAtAction(nameof(CreateVersionEntry), new { id = createdEntry.EntryId }, new { id = createdEntry.EntryId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while creating the version entry");
            return StatusCode(500, "An error occurred while creating the version entry.");
        }
    }
    
    [HttpGet("{id}/archive")]
    public async Task<IActionResult> GetVersionArchive(string id)
    {
        try
        {
            var versionEntry = await databaseService.GetVersionEntryByIdAsync(id);
            if (versionEntry == null)
            {
                logger.LogWarning("Version entry not found for ID: {EntryId}", id);
                return NotFound($"Version entry with ID {id} not found.");
            }

            (string FileName, Stream ArchiveStream)? zipArchiveAsync = await databaseService.GetZipArchiveAsync(versionEntry.ArtifactsArchiveId);
            if (zipArchiveAsync is { ArchiveStream: null })
            {
                logger.LogWarning("Archive not found for version entry ID: {EntryId}", id);
                return NotFound($"Archive for version entry with ID {id} not found.");
            }

            logger.LogInformation("Returning archive for version entry ID: {EntryId}", id);
            return File(zipArchiveAsync?.ArchiveStream!, "application/zip", zipArchiveAsync?.FileName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while retrieving the archive for version entry ID: {EntryId}", id);
            return StatusCode(500, "An error occurred while retrieving the archive.");
        }
    }
    
    [HttpGet("{appName}/latest/archive")]
    public async Task<IActionResult> GetLatestVersionArchive(string appName)
    {
        try
        {
            var latestVersion = await databaseService.GetLatestVersionEntryAsync(appName);
            if (latestVersion == null)
            {
                logger.LogWarning("No version entries found for app: {AppName}", appName);
                return NotFound($"No version entries found for app {appName}.");
            }

            (string FileName, Stream ArchiveStream)? zipArchiveAsync = await databaseService.GetZipArchiveAsync(latestVersion.ArtifactsArchiveId);
            if (zipArchiveAsync is { ArchiveStream: null })
            {
                logger.LogWarning("Archive not found for latest version of app: {AppName}", appName);
                return NotFound($"Archive for latest version of app {appName} not found.");
            }

            logger.LogInformation("Returning archive for latest version of app: {AppName}", appName);
            return File(zipArchiveAsync?.ArchiveStream!, "application/zip", zipArchiveAsync?.FileName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while retrieving the archive for the latest version of app: {AppName}", appName);
            return StatusCode(500, "An error occurred while retrieving the archive.");
        }
    }
    
    [HttpGet("{appName}/current-version")]
    public async Task<IActionResult> GetCurrentVersion(string appName)
    {
        try
        {
            var latestVersion = await databaseService.GetLatestVersionEntryAsync(appName);
            if (latestVersion == null)
            {
                logger.LogWarning("No version entries found for app: {AppName}", appName);
                return NotFound($"No version entries found for app {appName}.");
            }

            var currentVersion = new
            {
                latestVersion.AppName,
                latestVersion.MajorVersion,
                latestVersion.MinorVersion,
                latestVersion.PatchVersion,
                latestVersion.Date,
                latestVersion.Notes
            };

            logger.LogInformation("Returning current version for app: {AppName}", appName);
            return Ok(currentVersion);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while retrieving the current version for app: {AppName}", appName);
            return StatusCode(500, "An error occurred while retrieving the current version.");
        }
    }
    
    [HttpGet("all")]
    public async Task<IActionResult> GetAllVersionEntries()
    {
        try
        {
            var allVersions = await databaseService.GetAllVersionEntriesAsync();
            var formattedVersions = allVersions.Select(v => $"{v.AppName} (v.{v.MajorVersion}.{v.MinorVersion}.{v.PatchVersion})");

            logger.LogInformation("Returning all version entries");
            return Ok(string.Join("\n", formattedVersions));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while retrieving all version entries");
            return StatusCode(500, "An error occurred while retrieving all version entries.");
        }
    }
}