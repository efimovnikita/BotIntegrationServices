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
    private const int GlobalTrackCountLimit = 50;

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
                logger.LogWarning("Too large list of videos");
                throw new Exception("The playlist has too many music tracks.");
            }

            logger.LogInformation("Getting playlist videos...");
            foreach (var video in videos)
            {
                var url = video.Url;
                var manifest = await youtube.Videos.Streams.GetManifestAsync(url);
                var streamInfo = manifest.GetAudioOnlyStreams().TryGetWithHighestBitrate();
                if (streamInfo == null)
                {
                    continue;
                }

                var filePath = Path.Combine(Path.GetTempPath(), $"{video.Title}.mp3");
                logger.LogInformation("Downloading file {FilePath}", filePath);
                if (FilePathHasInvalidChars(filePath))
                {
                    filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp3");
                }

                try
                {
                    await youtube.Videos.Streams.DownloadAsync(streamInfo, filePath);
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "An error occurred while downloading file {FilePath}", filePath);
                    continue;
                }

                if (System.IO.File.Exists(filePath))
                {
                    files.Add(filePath);
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

            // upload
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

            var httpClient = clientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromMinutes(10);

            using var uploadEndHealthResponse = await httpClient.SendAsync(uploadEndHealthRequest);
            if (uploadEndHealthResponse.IsSuccessStatusCode == false)
            {
                throw new Exception(
                    $"The upload endpoint health response status code {uploadEndHealthResponse.StatusCode}");
            }

            var authServer = configuration["Urls:AuthServer"];
            var clientId = configuration["Configuration:ClientId"];
            var clientSecret = configuration["Configuration:ClientSecret"];

            if ((authServer != null && clientId != null && clientSecret != null) == false)
            {
                logger.LogWarning("Auth server parameter is missing.");
                throw new Exception("Authentication server configuration is invalid.");
            }

            var authData = await GetAuthData(httpClient, authServer, clientId, clientSecret);
            if (authData == null)
            {
                logger.LogWarning("Auth data could not be retrieved.");
                throw new Exception($"Authentication server returned an unauthorized response.");
            }

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

            using var uploadFileResponse = await httpClient.SendAsync(uploadFileRequest);
            if (uploadFileResponse.IsSuccessStatusCode == false)
            {
                throw new Exception($"The upload endpoint response status code {uploadFileResponse.StatusCode}");
            }

            var uploadResultStr = await uploadFileResponse.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(uploadResultStr))
            {
                logger.LogWarning("Upload data could not be retrieved.");
                throw new Exception("Unable to get upload data from file sharing server");
            }

            var uploadData = JsonSerializer.Deserialize<UploadData>(uploadResultStr);
            if (uploadData == null)
            {
                logger.LogWarning("Upload data could not be deserialized.");
                throw new Exception("Unable to parse upload data from file sharing server");
            }

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

    private static bool FilePathHasInvalidChars(string path)
    {
        return (!string.IsNullOrEmpty(path) && path.IndexOfAny(Path.GetInvalidPathChars()) >= 0);
    }
}