using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Validation.AspNetCore;
using OpenIddictUI.TemporaryTokens;

namespace OpenIddictUI.Controllers;

[ApiController]
[Route("api/temporary-tokens")]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
[IgnoreAntiforgeryToken]
public class TemporaryTokensController(TemporaryTokenService service) : ControllerBase
{
    [HttpPost]
    public IActionResult Create([FromBody] JsonElement input)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        if (input.ValueKind != JsonValueKind.Object || !AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header) ||
            !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(header.Parameter))
        {
            return BadRequest(ApiResult.Error(400, "Invalid request"));
        }

        var result = service.Create(header.Parameter, input);
        if (result.Status != 200)
        {
            return StatusCode(result.Status, ApiResult.Error(result.Status, result.Status == 400 ? "Invalid request" : "Token cannot be shared"));
        }
        return Ok(ApiResult.Ok(data: new
        {
            accessToken = result.Token,
            tokenType = "Bearer",
            expiresAt = result.ExpiresAt,
            scopes = result.Scopes
        }));
    }
}
