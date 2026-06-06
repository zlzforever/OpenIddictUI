using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace OpenIddictUI.Api.Controllers;

[ApiController]
[Authorize(Policy = "api1")]
[Route("api")]
public class DemoController(
    ILogger<DemoController> logger) : ControllerBase
{
    [HttpGet("me")]
    public IActionResult Me()
    {
        logger.LogInformation("Demo: /api/me called");
        var claims = User.Claims.Select(c => new { c.Type, c.Value });
        return Ok(new { message = "Authenticated!", claims });
    }
}