using System.Text.Json;
using BotIntegration.Services.YouTube.Models;
using Microsoft.AspNetCore.Mvc;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using Hangfire;
using Hangfire.Server;
using Refit;

namespace BotIntegration.Services.YouTube.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AudioController(
    IBackgroundJobClient backgroundJobClient,
    ILogger<AudioController> logger,
    IConfiguration configuration, 
    IFileSharingApi fileSharingApi,
    IAuthApi authApi) : ControllerBase
{
    private const string Result = "Result";
    private const string ExceptionMessage = "ExceptionMessage";
    private const int GlobalTrackCountLimit = 30;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string videoUrl)
    {
        if (string.IsNullOrEmpty(videoUrl))
        {
            logger.LogWarning("VideoUrl parameter is required.");
            return BadRequest("VideoUrl parameter is required.");
        }

        try
        {
            var youtube = new YoutubeClient();
            var video = await youtube.Videos.GetAsync(videoUrl);
            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl);
            var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

            var stream = await youtube.Videos.Streams.GetAsync(streamInfo);

            var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            return File(memoryStream, "audio/mp3", $"{video.Title}.mp3");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while processing the request: {VideoUrl}", videoUrl);
            return StatusCode(500, "An internal server error occurred. Please try again later.");
        }
    }
    
    [HttpPost("get-archive")]
    public IActionResult GetArchive([FromBody] UrlsRequest request)
    {
        try
        {
            if (request.Urls == null) throw new ArgumentNullException(nameof(request.Urls));

            if (request.Urls.All(url =>
                    string.IsNullOrWhiteSpace(url) == false && (url.StartsWith("https://music.youtube.com") ||
                                                                url.StartsWith("https://www.youtube.com"))) == false)
            {
                return BadRequest("All urls must start with https://music.youtube.com or with https://www.youtube.com");
            }

            var jobId = backgroundJobClient.Enqueue(() => PerformJob(request.Urls, null));
            
            return Ok(new { JobId = jobId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while getting the playlist audio archive.");
            return StatusCode(500, "An error occurred while getting the playlist audio archive.");
        }
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task PerformJob(string[] urls, [FromServices] PerformContext? context)
    {
        var files = new List<string>();
        var tempDirPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}");
        logger.LogInformation("Temp dir path: {Path}", tempDirPath);
        var zipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");
        logger.LogInformation("Zip path: {Path}", zipPath);
        
        try
        {
            var youtube = new YoutubeClient();

            if (urls.Length > GlobalTrackCountLimit)
            {
                logger.LogWarning("Urls count exceeds limit. Truncating to {Limit} limit.", GlobalTrackCountLimit);
                urls = urls.Take(GlobalTrackCountLimit).ToArray();
            }

            const int chunkSize = 3;
            var random = new Random();
            for (var i = 0; i < urls.Length; i += chunkSize)
            {
                var videoChunk = urls.Skip(i).Take(chunkSize);
                var downloadTasks = videoChunk.Select(async url =>
                {
                    var manifest = await youtube.Videos.Streams.GetManifestAsync(url);
                    var streamInfo = manifest.GetAudioOnlyStreams().TryGetWithHighestBitrate();
                    if (streamInfo == null)
                    {
                        return string.Empty;
                    }
                    
                    var video = await youtube.Videos.GetAsync(url);

                    // Sanitize the video title by replacing invalid filename characters with underscores
                    var sanitizedTitle = string.Join("_", video.Title.Split(Path.GetInvalidFileNameChars()));
                    
                    // Create the filename with .mp3 extension
                    var fileName = $"{sanitizedTitle}.mp3";
                    
                    // Truncate the filename if it exceeds the maximum allowed length (255 characters)
                    if (fileName.Length > 255)
                    {
                        fileName = $"{fileName.Substring(0, 251)}.mp3";
                    }
                    
                    // Combine the temporary path with the sanitized filename
                    var filePath = Path.Combine(Path.GetTempPath(), fileName);
                    logger.LogInformation("Downloading file {FilePath}", filePath);
                    
                    // Check if the file path contains any invalid characters
                    if (Path.GetInvalidPathChars().Any(c => filePath.Contains(c)))
                    {
                        // If invalid characters are found, generate a new filename using a GUID
                        filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp3");
                        logger.LogWarning("Invalid characters in file path. Using generated GUID: {FilePath}", filePath);
                    }

                    try
                    {
                        await youtube.Videos.Streams.DownloadAsync(streamInfo, filePath);
                        if (System.IO.File.Exists(filePath))
                        {
                            return filePath;
                        }
                    }
                    catch (Exception e)
                    {
                        logger.LogWarning(e, "An error occurred while downloading file {FilePath}", filePath);
                    }

                    return string.Empty;
                });

                var downloadedFiles = await Task.WhenAll(downloadTasks);
                files.AddRange(downloadedFiles.Where(f => !string.IsNullOrEmpty(f)));

                // Add a random delay between chunks
                if (i + chunkSize < urls.Length)
                {
                    var delay = random.Next(1000, 5000);
                    logger.LogInformation("Waiting for {Delay} ms before processing next chunk", delay);
                    await Task.Delay(delay);
                }
            }

            Directory.CreateDirectory(tempDirPath);

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var destFileName = Path.Combine(tempDirPath, fileName);
                System.IO.File.Move(file, destFileName);
            }

            System.IO.Compression.ZipFile.CreateFromDirectory(tempDirPath, zipPath);

            if (System.IO.File.Exists(zipPath) == false)
            {
                logger.LogInformation("Zip file could not be created.");
                throw new Exception("The zip file could not be created.");
            }

            var uploadData = await UploadZipFile(zipPath);
            logger.LogInformation("Upload data returned: {Data}", uploadData.FileUrl);
            context?.SetJobParameter(Result, uploadData.FileUrl);
        }
        catch (Exception e)
        {
            logger.LogError(e, "An error occurred during the background translation process");
            context?.SetJobParameter(ExceptionMessage, e.Message);

            throw;
        }
        finally
        {
            if (Directory.Exists(tempDirPath))
            {
                Directory.Delete(tempDirPath, true);
            }
            
            if (System.IO.File.Exists(zipPath))
            {
                System.IO.File.Delete(zipPath);
            }
        }
    }

    [HttpGet("get-status")]
    public IActionResult GetPlaylistJobStatus([FromQuery] string jobId)
    {
        using var connection = JobStorage.Current.GetConnection();
        var job = connection.GetJobData(jobId);
        if (job == null)
        {
            return NotFound("Job not found");
        }

        var status = job.State;
        var translationResult = "";
        var errorMessage = "";
        
        switch (status)
        {
            case "Succeeded":
            {
                var serializedResult = connection.GetJobParameter(jobId, Result);
                if (!string.IsNullOrEmpty(serializedResult))
                {
                    translationResult = JsonSerializer.Deserialize<string>(serializedResult);
                }

                break;
            }
            case "Failed":
            {
                var exceptionDetails = connection.GetJobParameter(jobId, ExceptionMessage);
                errorMessage = !string.IsNullOrEmpty(exceptionDetails)
                    ? JsonSerializer.Deserialize<string>(exceptionDetails)
                    : "An unknown error occurred during processing.";

                break;
            }
        }

        return Ok(new { Status = status, Result = translationResult, Error = errorMessage });
    }

    private async Task<UploadData> UploadZipFile(string zipPath)
    {
        logger.LogInformation("Starting UploadZipFile method with zipPath: {ZipPath}", zipPath);

        await fileSharingApi.CheckHealth();

        logger.LogDebug("Attempting to retrieve auth data");
        
        var data = new Dictionary<string, string> {
            { IAuthApi.GrantType, IAuthApi.ClientCredentials },
            { IAuthApi.ClientId, configuration["Configuration:ClientId"] ?? "" },
            { IAuthApi.ClientSecret, configuration["Configuration:ClientSecret"] ?? "" }
        };

        var authData = await authApi.GetAuthData(data);
        
        logger.LogInformation("Auth data retrieved successfully");
        
        var fileStream = System.IO.File.OpenRead(zipPath);
        var streamPart = new StreamPart(fileStream, Path.GetFileName(zipPath), "multipart/form-data");

        var uploadData = await fileSharingApi.UploadFile($"Bearer {authData.AccessToken}", streamPart);

        logger.LogInformation("File upload successful. UploadData: {@UploadData}", uploadData);
        return uploadData;
    }
}