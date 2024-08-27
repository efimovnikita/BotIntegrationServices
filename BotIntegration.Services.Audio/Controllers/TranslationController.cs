using System.Text.Json;
using CliWrap;
using Hangfire;
using Hangfire.Server;
using Microsoft.AspNetCore.Mvc;
using OpenAI;
using OpenAI.Audio;

namespace BotIntegration.Services.Audio.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TranslationController(IBackgroundJobClient backgroundJobClient, ILogger<TranslationController> logger) : ControllerBase
{
    private const string JobResultParameterName = "TranslationResult";
    private const string JobExceptionParameterName = "ExceptionMessage";

    [HttpPost("to-english")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 167_772_160, ValueLengthLimit = 167_772_160)]
    public async Task<IActionResult> UploadFile([FromForm] IFormFile audioFile, [FromForm] string openaiApiKey, [FromForm] string? prompt)
    {
        try
        {
            if (audioFile.Length == 0)
            {
                logger.LogWarning("File is null or empty");
                return BadRequest("File is empty");
            }

            var sizeInBytes = audioFile.Length;
            var sizeInMegabytes = (double) sizeInBytes / (1024 * 1024);

            var fileName = audioFile.FileName;
            logger.LogInformation("Received file: {FileName}, Size: {Length} megabytes", fileName, sizeInMegabytes);
            
            if (sizeInMegabytes >= 160)
            {
                logger.LogWarning("File is too big");
                return BadRequest("File is too big");
            }

            var extension = Path.GetExtension(fileName);
            if (extension.Equals(".mp3") == false)
            {
                logger.LogWarning("We are working only with mp3 files");
                return BadRequest("We are working only with mp3 files");
            }

            // first of all - save the file
            var uniqueFileName = Guid.NewGuid() + extension;
            var filePath = Path.Combine(Path.GetTempPath(), uniqueFileName);
            logger.LogInformation("File will be saved as: {filePath}", filePath);
            
            await using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                logger.LogInformation("Starting file copy");
                await audioFile.CopyToAsync(fileStream);
                logger.LogInformation("File copy completed");
            }
            
            // start the background job
            var jobId = backgroundJobClient.Enqueue(() =>
                PerformTranslation(openaiApiKey, prompt, sizeInMegabytes, filePath,null));
            
            return Ok(new { JobId = jobId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while translating the audio.");
            return StatusCode(500, "An error occurred while translating the audio");
        }
    }

    [AutomaticRetry(Attempts = 0)]
    public void PerformTranslation(string openaiApiKey, string? prompt, double sizeInMegabytes, string filePath, PerformContext? context)
    {
        try
        {
            var task = EncodeAndSendToOpenAi(openaiApiKey, prompt, sizeInMegabytes, filePath);
            var result = task.Result;
            context?.SetJobParameter(JobResultParameterName, result);
            
            if (string.IsNullOrEmpty(result))
            {
                throw new InvalidOperationException("Translation failed or returned an empty result");
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "An error occurred during the background translation process");
            context?.SetJobParameter(JobExceptionParameterName, e.Message);

            throw;
        }
    }
    
    [HttpGet("status")]
    public IActionResult GetStatus([FromQuery] string id)
    {
        using var connection = JobStorage.Current.GetConnection();
        var job = connection.GetJobData(id);
        if (job == null)
        {
            return NotFound("Job not found");
        }

        var status = job.State;
        var translationResult = "";
        var errorMessage = "";
        
        if (status == "Succeeded")
        {
            var serializedResult = connection.GetJobParameter(id, JobResultParameterName);
            if (!string.IsNullOrEmpty(serializedResult))
            {
                translationResult = JsonSerializer.Deserialize<string>(serializedResult);
            }
        }
        else if (status == "Failed")
        {
            var exceptionDetails = connection.GetJobParameter(id, JobExceptionParameterName);
            if (!string.IsNullOrEmpty(exceptionDetails))
            {
                errorMessage = JsonSerializer.Deserialize<string>(exceptionDetails);
            }
            else
            {
                errorMessage = "An unknown error occurred during processing.";
            }
        }

        return Ok(new { Status = status, Result = translationResult, Error = errorMessage });
    }

    private async Task<string> EncodeAndSendToOpenAi(string openaiApiKey, string? prompt, double sizeInMegabytes,
        string filePath)
    {
        if (sizeInMegabytes >= 24.5)
        {
            // need to encode in order to reduce the size
            var uniqueFileNameForOutput = Guid.NewGuid() + ".ogg";

            var result = await Cli.Wrap("ffmpeg")
                .WithArguments([
                    "-i", $"{filePath}", "-vn", "-map_metadata", "-1", "-ac", "1", "-c:a", "libopus", "-b:a", "12k",
                    "-application", "voip", $"{uniqueFileNameForOutput}"
                ])
                .WithWorkingDirectory(Path.GetTempPath())
                .ExecuteAsync();

            logger.LogInformation("Encoding result: {Result}", result.ExitCode);

            if (result.ExitCode != 0)
            {
                logger.LogWarning("File encoding result was unsuccessful");
                return "";
            }

            var encodedFilePath = Path.Combine(Path.GetTempPath(), uniqueFileNameForOutput);
            if (System.IO.File.Exists(encodedFilePath) == false)
            {
                logger.LogWarning("Error getting the encoded file");
                return "";
            }

            var encodedFileInfo = new FileInfo(encodedFilePath);
            var encodedFileLengthInMbs = (double)encodedFileInfo.Length / (1024 * 1024);
            if (encodedFileLengthInMbs >= 24.5)
            {
                logger.LogWarning("Encoded file is too big");
                return "";
            }

            filePath = encodedFilePath;
        }

        logger.LogInformation("This file will be send to OpenAI: {Path}", filePath);

        AudioClient client = new(model: "whisper-1", openaiApiKey,
            options: new OpenAIClientOptions { NetworkTimeout = TimeSpan.FromMinutes(10) });

        AudioTranslationOptions options = new()
        {
            Prompt = string.IsNullOrWhiteSpace(prompt) == false ? prompt : "",
            ResponseFormat = AudioTranslationFormat.Verbose
        };

        var translationResult = await client.TranslateAudioAsync(filePath, options);
        var text = translationResult.Value.Text;

        return text;
    }
}