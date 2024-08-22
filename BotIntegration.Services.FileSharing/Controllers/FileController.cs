using Microsoft.AspNetCore.Mvc;

namespace BotIntegration.Services.FileSharing.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FileController(IWebHostEnvironment environment, IConfiguration configuration) : ControllerBase
{
    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("File is empty");
        }

        var uploadsFolder = Path.Combine(environment.WebRootPath, "uploads");
        if (!Directory.Exists(uploadsFolder))
        {
            Directory.CreateDirectory(uploadsFolder);
        }

        var uniqueFileName = Guid.NewGuid() + Path.GetExtension(file.FileName);
        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

        await using (var fileStream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(fileStream);
        }

        var fileUrl = $"{configuration["Urls:GatewayServer"]}{uniqueFileName}";  
        return Ok(new { fileUrl });
    }
}