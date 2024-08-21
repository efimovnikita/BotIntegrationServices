using Microsoft.AspNetCore.Mvc;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace BotIntegration.Services.YouTube.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AudioController(ILogger<AudioController> logger) : ControllerBase
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
}