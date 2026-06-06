using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace OpenIddictUI.Controllers;

[Route("api/antiforgery")]
public class AntiforgeryController(IAntiforgery antiforgery) : Controller
{
    [HttpGet("token")]
    public IActionResult Token()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new TokenResponse { Token = tokens.RequestToken });
    }

    private class TokenResponse
    {
        public string? Token { get; set; }
    }
}