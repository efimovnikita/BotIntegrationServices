using System.Net.Http.Headers;
using System.Text.Json;
using BotIntegration.Services.YouTube.Models;
using Microsoft.AspNetCore.Mvc;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using Hangfire;
using Hangfire.Server;
using YoutubeExplode.Common;

namespace BotIntegration.Services.YouTube.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AudioController(IBackgroundJobClient backgroundJobClient, ILogger<AudioController> logger, IConfiguration configuration, IHttpClientFactory clientFactory) : ControllerBase
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

    [HttpGet("get-playlist-archive")]
    public IActionResult GetPlaylistArchive([FromQuery] string playlistUrl)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(playlistUrl))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(playlistUrl));

            if (playlistUrl.StartsWith("https://music.youtube.com/playlist") == false)
            {
                return BadRequest("You need to provide an YouTube playlist URL.");
            }

            var jobId = backgroundJobClient.Enqueue(() => PerformJob(playlistUrl, null));
            
            return Ok(new { JobId = jobId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while getting the playlist audio archive.");
            return StatusCode(500, "An error occurred while getting the playlist audio archive.");
        }
    }

    [HttpGet("get-playlist-job-status")]
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

    [AutomaticRetry(Attempts = 0)]
    public async Task PerformJob(string playlistUrl, PerformContext? context)
    {
        var files = new List<string>();
        var tempDirPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}");
        logger.LogInformation("Temp dir path: {Path}", tempDirPath);
        var zipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");
        logger.LogInformation("Zip path: {Path}", zipPath);
        
        try
        {
            var youtube = new YoutubeClient();

            var videos = await youtube.Playlists.GetVideosAsync(playlistUrl);
            logger.LogInformation("Retrieving playlist videos...");
            
            if (videos.Count > GlobalTrackCountLimit)
            {
                logger.LogWarning("Playlist exceeds limit. Truncating to {Limit} videos.", GlobalTrackCountLimit);
                videos = videos.Take(GlobalTrackCountLimit).ToList();
            }

            logger.LogInformation("Getting playlist videos...");
            const int chunkSize = 3;
            var random = new Random();
            for (var i = 0; i < videos.Count; i += chunkSize)
            {
                var videoChunk = videos.Skip(i).Take(chunkSize);
                var downloadTasks = videoChunk.Select(async video =>
                {
                    var url = video.Url;
                    var manifest = await youtube.Videos.Streams.GetManifestAsync(url);
                    var streamInfo = manifest.GetAudioOnlyStreams().TryGetWithHighestBitrate();
                    if (streamInfo == null)
                    {
                        return string.Empty;
                    }

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
                if (i + chunkSize < videos.Count)
                {
                    var delay = random.Next(1000, 5000); // Random delay between 1 to 5 seconds
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
    
    private async Task<AuthData?> GetAuthData(HttpClient httpClient, string authServer, string clientId,
        string clientSecret)
    {
        try
        {
            var authRequest = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(authServer),
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "grant_type", "client_credentials" },
                    { "client_id", clientId },
                    { "client_secret", clientSecret },
                })
            };
            using var authResponse = await httpClient.SendAsync(authRequest);
            if (authResponse.IsSuccessStatusCode == false)
            {
                return null;
            }

            var authStr = await authResponse.Content.ReadAsStringAsync();

            if (string.IsNullOrEmpty(authStr))
            {
                return null;
            }

            return JsonSerializer.Deserialize<AuthData>(authStr);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while getting the auth data");
            return null;
        }
    }

    private async Task<UploadData> UploadZipFile(string zipPath)
    {
        logger.LogInformation("Starting UploadZipFile method with zipPath: {ZipPath}", zipPath);

        var httpClient = clientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromMinutes(10);

        await CheckFileSharingEndpointHealth(httpClient);

        var authServer = configuration["Urls:AuthServer"];
        var clientId = configuration["Configuration:ClientId"];
        var clientSecret = configuration["Configuration:ClientSecret"];

        if ((authServer != null && clientId != null && clientSecret != null) == false)
        {
            logger.LogWarning("Auth server parameter is missing.");
            throw new Exception("Authentication server configuration is invalid.");
        }

        logger.LogDebug("Attempting to retrieve auth data");
        var authData = await GetAuthData(httpClient, authServer, clientId, clientSecret);
        if (authData == null)
        {
            logger.LogWarning("Auth data could not be retrieved.");
            throw new Exception($"Authentication server returned an unauthorized response.");
        }
        logger.LogInformation("Auth data retrieved successfully");

        var uploadData = await UploadFileToFileSharingService(httpClient, authData, zipPath);

        logger.LogInformation("File upload successful. UploadData: {@UploadData}", uploadData);
        return uploadData;
    }

    private async Task CheckFileSharingEndpointHealth(HttpClient httpClient)
    {
        var fileSharingEndpointHealthUrl = configuration["Urls:FileSharingEndpointHealth"];
        if (fileSharingEndpointHealthUrl == null)
        {
            logger.LogWarning("FileSharingEndpointHealthUrl parameter is missing.");
            throw new Exception("Unable to determine the health status of the file sharing endpoint");
        }

        var uploadEndHealthRequest = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri(fileSharingEndpointHealthUrl),
        };

        logger.LogDebug("Sending health check request to {Url}", fileSharingEndpointHealthUrl);
        using var uploadEndHealthResponse = await httpClient.SendAsync(uploadEndHealthRequest);
        if (uploadEndHealthResponse.IsSuccessStatusCode == false)
        {
            logger.LogError("Health check failed with status code {StatusCode}", uploadEndHealthResponse.StatusCode);
            throw new Exception(
                $"The upload endpoint health response status code {uploadEndHealthResponse.StatusCode}");
        }
        logger.LogInformation("Health check successful");
    }

    private async Task<UploadData> UploadFileToFileSharingService(HttpClient httpClient, AuthData authData, string zipPath)
    {
        var fileSharingEndpointUrl = configuration["Urls:FileSharingEndpoint"];
        if (fileSharingEndpointUrl == null)
        {
            logger.LogWarning("FileSharingEndpointUrl parameter is missing.");
            throw new Exception("Unable to determine the health status of the file sharing endpoint");
        }

        var uploadFileRequest = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri(fileSharingEndpointUrl),
            Headers =
            {
                { "Authorization", $"Bearer {authData.AccessToken}" },
            }
        };

        var content = new MultipartFormDataContent();
        var fileBytes = await System.IO.File.ReadAllBytesAsync(zipPath);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");
        content.Add(fileContent, "file", Path.GetFileName(zipPath));
        uploadFileRequest.Content = content;

        logger.LogDebug("Sending file upload request to {Url}", fileSharingEndpointUrl);
        using var uploadFileResponse = await httpClient.SendAsync(uploadFileRequest);
        if (uploadFileResponse.IsSuccessStatusCode == false)
        {
            logger.LogError("File upload failed with status code {StatusCode}", uploadFileResponse.StatusCode);
            throw new Exception($"The upload endpoint response status code {uploadFileResponse.StatusCode}");
        }

        var uploadResultStr = await uploadFileResponse.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(uploadResultStr))
        {
            logger.LogWarning("Upload data could not be retrieved.");
            throw new Exception("Unable to get upload data from file sharing server");
        }

        logger.LogDebug("Attempting to deserialize upload data");
        var uploadData = JsonSerializer.Deserialize<UploadData>(uploadResultStr);
        if (uploadData == null)
        {
            logger.LogWarning("Upload data could not be deserialized.");
            throw new Exception("Unable to parse upload data from file sharing server");
        }

        return uploadData;
    }
}