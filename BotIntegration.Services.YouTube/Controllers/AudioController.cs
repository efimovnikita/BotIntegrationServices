using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Polly;

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
            var downloadedFilePath = await DownloadAudioUsingPlaywright(videoUrl);

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

    private async Task<string> DownloadAudioUsingPlaywright(string videoUrl)
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        logger.LogInformation("Creating download directory at {DownloadPath}", downloadPath);
        Directory.CreateDirectory(downloadPath);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            DownloadsPath = downloadPath
        });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            AcceptDownloads = true
        });
        var page = await context.NewPageAsync();

        var providerUrl = configuration["Provider"] ?? "";
        logger.LogInformation("Navigating to provider URL: {ProviderUrl}", providerUrl);
        await page.GotoAsync(providerUrl);

        // Fill in the video URL and click the submit button
        logger.LogInformation("Filling in video URL and clicking submit button.");
        await page.FillAsync("#videoUrl", videoUrl);
        await page.ClickAsync("#videoBtn");

        // Define retry policy
        var retryPolicy = Policy
            .Handle<TimeoutException>()
            .Or<PlaywrightException>()
            .WaitAndRetryAsync(
                3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (exception, timeSpan, retryCount, _) =>
                {
                    logger.LogWarning(exception,
                        "Attempt {RetryCount} failed to find 'Extract Audio' link. Retrying in {RetryInterval}s.",
                        retryCount, timeSpan.TotalSeconds);
                }
            );

        // Wait for the "Extract Audio" link to appear with retry logic
        logger.LogInformation("Waiting for 'Extract Audio' link to appear.");
        await retryPolicy.ExecuteAsync(async () =>
        {
            await page.WaitForSelectorAsync("a.btn.btn-success.js-download:has-text('Extract Audio')",
                new PageWaitForSelectorOptions
                {
                    Timeout = 30000
                });
        });

        // Set up a task to wait for the download to complete
        var downloadTaskCompletionSource = new TaskCompletionSource<string>();
        var downloadedFilePath = string.Empty;

        page.Download += (_, download) =>
        {
            downloadedFilePath = Path.Combine(downloadPath, download.SuggestedFilename);
            logger.LogInformation("Download started for file: {FileName}", download.SuggestedFilename);
            download.SaveAsAsync(downloadedFilePath)
                .ContinueWith(_ => downloadTaskCompletionSource.SetResult(downloadedFilePath));
        };

        // Use JavaScript to wait for the dropdown, click it, wait for the MP3 link, and click it
        logger.LogInformation("Executing JavaScript to handle dropdown and MP3 link.");
        await page.EvaluateAsync("""
                                  async () => {
                                      // Wait for the dropdown button to appear
                                      await new Promise(resolve => {
                                          const checkElement = setInterval(() => {
                                              const button = document.querySelector('button.btn.btn-success.dropdown-toggle');
                                              if (button && button.textContent.includes('m4a')) {
                                                  clearInterval(checkElement);
                                                  resolve(button);
                                              }
                                          }, 100);
                                      });
                                 
                                      // Click the dropdown button
                                      document.querySelector('button.btn.btn-success.dropdown-toggle').click();
                                 
                                      // Wait for the MP3 download link to appear
                                      const mp3Link = await new Promise(resolve => {
                                          const checkLink = setInterval(() => {
                                              const link = document.querySelector('a.js-download[data-format="mp3"]');
                                              if (link && link.textContent.includes('mp3 (quality: 48 kHz)')) {
                                                  clearInterval(checkLink);
                                                  resolve(link);
                                              }
                                          }, 100);
                                      });
                                 
                                      // Click the MP3 download link
                                      mp3Link.click();
                                  }
                                 """);

        // Wait for the download to complete or timeout after 4 minutes
        logger.LogInformation("Waiting for download to complete or timeout after 4 minutes.");
        var downloadTask =
            await Task.WhenAny(downloadTaskCompletionSource.Task, Task.Delay(TimeSpan.FromMinutes(4)));

        if (downloadTask != downloadTaskCompletionSource.Task)
        {
            logger.LogError("Download did not complete within the expected time.");
            throw new TimeoutException("Download did not complete within the expected time.");
        }

        return downloadedFilePath;
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
            var downloadedFilePath = await DownloadAudioUsingPlaywright(videoUrl);
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