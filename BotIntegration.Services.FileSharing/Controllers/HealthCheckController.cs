using Microsoft.AspNetCore.Mvc;

namespace BotIntegration.Services.FileSharing.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthCheckController(ILogger<HealthCheckController> logger) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        logger.LogInformation("Health check endpoint was called.");
        return Ok(new { status = "Healthy" });
    }
}