using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BotIntegration.Services.YouTube.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AudioController(
    ILogger<AudioController> logger,
    IConfiguration configuration) : ControllerBase
{
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
            var downloadedFilePath = await DownloadAudioUsingYtDlp(videoUrl);
            
            // Return the MP3 file
            logger.LogInformation("Download completed. Returning the MP3 file.");
            var fileStream = new FileStream(downloadedFilePath, FileMode.Open, FileAccess.Read);
            return File(fileStream, "audio/mpeg", $"{Guid.NewGuid()}.mp3");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while processing the request: {VideoUrl}", videoUrl);
            return StatusCode(500, "An internal server error occurred. Please try again later.");
        }
    }
    
    private async Task<string> DownloadAudioUsingYtDlp(string videoUrl)
    {
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputDir);
        var outputTemplate = Path.Combine(outputDir, "%(title)s.%(ext)s");
        var ytDlpPath = Path.Combine("Tools", "yt-dlp_linux");
        var proxy = configuration["Urls:Proxy"];

        var arguments = $"--proxy {proxy} " +
                        "--user-agent \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36\" " +
                        "-f \"worstaudio[ext=m4a]/worstaudio/worst\" -x --audio-format mp3 " +
                        $"-o \"{outputTemplate}\" " +
                        $"\"{videoUrl}\"";

        logger.LogInformation("Running yt-dlp with arguments: {Arguments}", arguments);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            logger.LogError("yt-dlp process failed with exit code {ExitCode}. Error: {Error}", process.ExitCode, error);
            throw new Exception($"yt-dlp process failed with exit code {process.ExitCode}. Error: {error}");
        }

        var downloadedFiles = Directory.GetFiles(outputDir, "*.mp3");
        if (downloadedFiles.Length == 0)
        {
            throw new Exception("No MP3 files were downloaded.");
        }

        var outputFilePath = downloadedFiles[0]; // Get the first downloaded file
        logger.LogInformation("Audio download completed. File path: {FilePath}", outputFilePath);
        return outputFilePath;
    }
    
    [HttpGet("get-split-audio")]
    public async Task<IActionResult> Get([FromQuery] string videoUrl, [FromQuery] TimeSpan? startTime,
        [FromQuery] TimeSpan? endTime)
    {
        if (string.IsNullOrEmpty(videoUrl))
        {
            logger.LogWarning("VideoUrl parameter is required.");
            return BadRequest("VideoUrl parameter is required.");
        }

        try
        {
            logger.LogInformation("Starting audio download using Playwright for video URL: {VideoUrl}", videoUrl);
            var downloadedFilePath = await DownloadAudioUsingYtDlp(videoUrl);
            logger.LogInformation("Audio download completed. File path: {FilePath}", downloadedFilePath);

            var memoryStream = new MemoryStream();
            using (var fileStream = new FileStream(downloadedFilePath, FileMode.Open, FileAccess.Read))
            {
                logger.LogInformation("Copying downloaded file to memory stream.");
                await fileStream.CopyToAsync(memoryStream);
            }

            memoryStream.Position = 0;

            logger.LogInformation("Trimming audio stream from {StartTime} to {EndTime}", startTime, endTime);
            memoryStream = await TrimAudioStream(memoryStream, startTime, endTime);

            var fileName = $"{Guid.NewGuid()}.mp3";
            logger.LogInformation("Returning trimmed audio file: {FileName}", fileName);
            return File(memoryStream, "audio/mp3", fileName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while processing the request: {VideoUrl}", videoUrl);
            return StatusCode(500, "An internal server error occurred. Please try again later.");
        }
    }

    private async Task<MemoryStream> TrimAudioStream(MemoryStream originalStream, TimeSpan? startTime,
        TimeSpan? endTime)
    {
        var inputFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.webm");
        var outputFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp3");

        try
        {
            // Write the original stream to a temporary file
            logger.LogInformation("Writing the original stream to a temporary file: {InputFile}", inputFile);
            using (var fileStream = new FileStream(inputFile, FileMode.Create, FileAccess.Write))
            {
                originalStream.Position = 0;
                await originalStream.CopyToAsync(fileStream);
            }

            // Prepare FFmpeg arguments
            var arguments = $"-i \"{inputFile}\"";

            if (startTime.HasValue)
            {
                arguments += $" -ss {startTime.Value:hh\\:mm\\:ss\\.fff}";
            }

            if (endTime.HasValue)
            {
                arguments += $" -to {endTime.Value:hh\\:mm\\:ss\\.fff}";
            }

            arguments += $" -c:a libmp3lame -q:a 2 \"{outputFile}\""; // Re-encode to MP3 format

            logger.LogInformation("Running FFmpeg with arguments: {Arguments}", arguments);

            // Run FFmpeg
            using (var process = new Process())
            {
                process.StartInfo.FileName = "ffmpeg"; // Make sure ffmpeg is in your PATH
                process.StartInfo.Arguments = arguments;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                process.Start();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    logger.LogError("FFmpeg process failed with exit code {ExitCode}. Error: {Error}", process.ExitCode, error);
                    throw new Exception($"FFmpeg process failed with exit code {process.ExitCode}. Error: {error}");
                }
            }

            // Read the output file into a new MemoryStream
            logger.LogInformation("Reading the output file into a new MemoryStream: {OutputFile}", outputFile);
            var resultStream = new MemoryStream();
            using (var fileStream = new FileStream(outputFile, FileMode.Open, FileAccess.Read))
            {
                await fileStream.CopyToAsync(resultStream);
            }

            resultStream.Position = 0;
            logger.LogInformation("Returning the result stream.");
            return resultStream;
        }
        finally
        {
            // Clean up temporary files
            if (System.IO.File.Exists(inputFile))
                System.IO.File.Delete(inputFile);
            if (System.IO.File.Exists(outputFile))
                System.IO.File.Delete(outputFile);
        }
    }
}