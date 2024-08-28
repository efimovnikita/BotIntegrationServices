using System.Text.Json;
using Hangfire;
using Hangfire.Server;
using Microsoft.AspNetCore.Mvc;
using OpenAI;
using OpenAI.Audio;

namespace BotIntegration.Services.Audio.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TranscriptionController(IBackgroundJobClient backgroundJobClient, ILogger<TranscriptionController> logger) : ControllerBase
{

    [HttpPost("get-text")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = Shared.LengthLimit, ValueLengthLimit = Shared.LengthLimit)]
    public async Task<IActionResult> GetTranscription([FromForm] IFormFile audioFile, [FromForm] string openaiApiKey, [FromForm] string? prompt)
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

            if (sizeInMegabytes >= Shared.MaxSizeInMbs)
            {
                logger.LogWarning("File is too big");
                return BadRequest("File is too big");
            }

            var extension = Path.GetExtension(fileName);
            if (extension.Equals(Shared.AllowedExtension) == false)
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
                PerformJob(openaiApiKey, prompt, sizeInMegabytes, filePath,null));
            
            return Ok(new { JobId = jobId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while transcribing the audio.");
            return StatusCode(500, "An error occurred while transcribing the audio");
        }
    }

    [AutomaticRetry(Attempts = 0)]
    public void PerformJob(string openaiApiKey, string? prompt, double sizeInMegabytes, string filePath, PerformContext? context)
    {
        try
        {
            var task = EncodeAndSendToOpenAi(openaiApiKey, prompt, sizeInMegabytes, filePath);
            var result = task.Result;
            context?.SetJobParameter(Shared.JobResultParameterName, result);
            
            if (string.IsNullOrEmpty(result))
            {
                throw new InvalidOperationException("Transcription failed or returned an empty result");
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "An error occurred during the background transcription process");
            context?.SetJobParameter(Shared.JobExceptionParameterName, e.Message);

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
        var result = "";
        var errorMessage = "";
        
        switch (status)
        {
            case "Succeeded":
            {
                var serializedResult = connection.GetJobParameter(id, Shared.JobResultParameterName);
                if (!string.IsNullOrEmpty(serializedResult))
                {
                    result = JsonSerializer.Deserialize<string>(serializedResult);
                }

                break;
            }
            case "Failed":
            {
                var exceptionDetails = connection.GetJobParameter(id, Shared.JobExceptionParameterName);
                errorMessage = !string.IsNullOrEmpty(exceptionDetails)
                    ? JsonSerializer.Deserialize<string>(exceptionDetails)
                    : "An unknown error occurred during processing.";

                break;
            }
        }

        return Ok(new { Status = status, Result = result, Error = errorMessage });
    }

    private async Task<string> EncodeAndSendToOpenAi(string openaiApiKey, string? prompt, double sizeInMegabytes,
        string filePath)
    {
        if (sizeInMegabytes >= Shared.MaxEncodeLimit)
        {
            // need to encode in order to reduce the size
            filePath = await Shared.CompressHugeFile(filePath, logger);
        }

        logger.LogInformation("This file will be send to OpenAI: {Path}", filePath);

        AudioClient client = new(model: Shared.ModelName, openaiApiKey,
            options: new OpenAIClientOptions { NetworkTimeout = Shared.Timeout });

        AudioTranscriptionOptions options = new()
        {
            Prompt = string.IsNullOrWhiteSpace(prompt) == false ? prompt : "",
            ResponseFormat = AudioTranscriptionFormat.Verbose,
            Granularities = AudioTimestampGranularities.Word | AudioTimestampGranularities.Segment,
        };

        var result = await client.TranscribeAudioAsync(filePath, options);
        var text = result.Value.Text;

        return text;
    }
}