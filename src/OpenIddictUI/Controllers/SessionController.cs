using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace OpenIddictUI.Controllers;

[Route("session")]
public class SessionController(
    IOpenIddictAuthorizationManager authorizationManager,
    IOpenIddictApplicationManager applicationManager,
    ILogger<SessionController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> GetInfo()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized(Errors.NotAuthenticated);
        }
        var claims = User.Claims
            .Where(c => c.Type != "AspNet.Identity.SecurityStamp"
                        && !c.Type.StartsWith("oi_"))
            .Select(c => new { type = ShortName(c.Type), value = c.Value })
            .ToList();

        var sub = User.FindFirst(Claims.Subject)?.Value;
        var clients = new List<object>();

        if (sub != null)
        {
            var authorizations = authorizationManager.FindAsync(
                subject: sub,
                client: null,
                status: Statuses.Valid,
                type: null,
                scopes: null
            );

            await foreach (var auth in authorizations)
            {
                var appId = await authorizationManager.GetApplicationIdAsync(auth);
                if (appId == null)
                {
                    continue;
                }

                var app = await applicationManager.FindByIdAsync(appId);
                if (app == null)
                {
                    logger.LogDebug("Session: app not found for auth id {AppId}", appId);
                    continue;
                }

                var scopes = await authorizationManager.GetScopesAsync(auth);
                clients.Add(new
                {
                    clientId = await applicationManager.GetClientIdAsync(app),
                    displayName = await applicationManager.GetDisplayNameAsync(app),
                    scopes = scopes.ToList()
                });
            }
        }

        return Ok(new ApiResult
        {
            Code = 200,
            Data = new { claims, clients }
        });
    }

    private static string ShortName(string type)
    {
        var idx = type.LastIndexOf('/');
        return idx >= 0 ? type[(idx + 1)..] : type;
    }
}
