using Microsoft.AspNetCore.Mvc;

namespace BotIntegration.Services.FileSharing.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FileController(IWebHostEnvironment environment, IConfiguration configuration, ILogger<FileController> logger) : ControllerBase
{
    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile(IFormFile? file)
    {
        logger.LogInformation("UploadFile method called");

        if (file == null || file.Length == 0)
        {
            logger.LogWarning("File is null or empty");
            return BadRequest("File is empty");
        }

        logger.LogInformation("Received file: {FileName}, Size: {Length} bytes", file.FileName, file.Length);

        try
        {
            var uploadsFolder = Path.Combine(environment.WebRootPath, "uploads");
            logger.LogInformation("Upload folder path: {UploadsFolder}", uploadsFolder);

            if (!Directory.Exists(uploadsFolder))
            {
                logger.LogInformation("Creating uploads folder");
                Directory.CreateDirectory(uploadsFolder);
            }

            var uniqueFileName = Guid.NewGuid() + Path.GetExtension(file.FileName);
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);
            logger.LogInformation("File will be saved as: {filePath}", filePath);

            await using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                logger.LogInformation("Starting file copy");
                await file.CopyToAsync(fileStream);
                logger.LogInformation("File copy completed");
            }

            var fileUrl = $"{configuration["Urls:GatewayServer"]}{uniqueFileName}";
            logger.LogInformation("File URL: {fileUrl}", fileUrl);

            return Ok(new { fileUrl });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while uploading the file");
            return StatusCode(500, "An error occurred while uploading the file");
        }
    }
}