using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BotIntegration.Services.Audio.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LanguageController(ILogger<TranscriptionController> logger) : ControllerBase
{
    [HttpPost]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = Shared.LengthLimit, ValueLengthLimit = Shared.LengthLimit)]
    public async Task<IActionResult> Get([FromForm] IFormFile audioFile)
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
            
            // Convert mp3 to wav using FFmpeg
            var wavFilePath = await ConvertMp3ToWav(filePath);
            logger.LogInformation("Converted file to WAV: {WavFilePath}", wavFilePath);

            // Execute whisper.cpp CLI tool
            var output = await ExecuteWhisperCli(wavFilePath);
            logger.LogInformation("Whisper CLI output: {Output}", output);

            return Content(output, "application/json");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while getting the language from the audio.");
            return StatusCode(500, "An error occurred while transcribing the audio");
        }
    }
    
    private async Task<string> ConvertMp3ToWav(string mp3FilePath)
    {
        var wavFilePath = Path.ChangeExtension(mp3FilePath, ".wav");
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-i {mp3FilePath} -acodec pcm_s16le -ar 16000 {wavFilePath}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process();
        process.StartInfo = processStartInfo;
        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new Exception($"FFmpeg conversion failed: {error}");
        }

        return wavFilePath;
    }
    
    private async Task<string> ExecuteWhisperCli(string filePath)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "/whisper.cpp/main",
            Arguments = $"-m /whisper.cpp/models/ggml-tiny.bin -f {filePath} -dl",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process();
        process.StartInfo = processStartInfo;
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var combinedOutput = output + error;
        logger.LogInformation("Whisper CLI combined output: {Output}", combinedOutput);

        // Parse the output to extract the language code
        var languageCode = ParseLanguageCode(combinedOutput);
        logger.LogInformation("Parsed language code: {LanguageCode}", languageCode);

        // Return JSON with the language code
        return System.Text.Json.JsonSerializer.Serialize(new { language = languageCode });
    }
    
    private string ParseLanguageCode(string output)
    {
        var lines = output.Split('\n');
        foreach (var line in lines)
        {
            if (line.Contains("whisper_full_with_state: auto-detected language:"))
            {
                var parts = line.Split(':');
                if (parts.Length > 2)
                {
                    var languagePart = parts[2].Trim();
                    return languagePart.Split(' ')[0]; // Extract the language code
                }
            }
        }
        return "unknown";
    }
}