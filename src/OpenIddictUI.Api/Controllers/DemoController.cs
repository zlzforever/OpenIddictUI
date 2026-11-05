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
    [OperationClassification(OperationClassification.ReadOnly)]
    public IActionResult Me()
    {
        logger.LogInformation("Demo: /api/me called");
        var claims = User.Claims.Select(c => new { c.Type, c.Value });
        return Ok(new { message = "Authenticated!", claims });
    }

    // Demonstration only: this endpoint never changes real data.
    [HttpPost("test-write")]
    [OperationClassification(OperationClassification.Write)]
    public IActionResult TestWrite() => Ok(new { message = "Write operation allowed" });

    [HttpGet("unclassified")]
    public IActionResult Unclassified() => Ok(new { message = "Unclassified operation" });
}
