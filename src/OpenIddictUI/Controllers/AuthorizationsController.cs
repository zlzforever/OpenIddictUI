using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;

namespace OpenIddictUI.Controllers;

[Authorize]
[Route("api/authorizations")]
public class AuthorizationsController(
    IOpenIddictAuthorizationManager authorizationManager,
    IOpenIddictApplicationManager applicationManager) : Controller
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var sub = User.FindFirstValue(OpenIddictConstants.Claims.Subject)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub))
        {
            return Ok(new ApiResult { Data = Array.Empty<object>() });
        }

        var auths = new List<(DateTimeOffset? Created, object Item)>();
        await foreach (var auth in authorizationManager.FindAsync(
            subject: sub, client: null, status: null, type: "permanent", scopes: null))
        {
            var appId = await authorizationManager.GetApplicationIdAsync(auth);
            var appName = appId != null
                ? await applicationManager.FindByIdAsync(appId) is { } app
                    ? await applicationManager.GetClientIdAsync(app) ?? "unknown"
                    : "unknown"
                : "unknown";
            var created = await authorizationManager.GetCreationDateAsync(auth);

            auths.Add((created, new
            {
                id = await authorizationManager.GetIdAsync(auth),
                clientId = appName,
                scopes = (await authorizationManager.GetScopesAsync(auth)).ToList(),
                type = await authorizationManager.GetTypeAsync(auth),
                status = await authorizationManager.GetStatusAsync(auth),
                created = created?.ToString("yyyy-MM-dd HH:mm:ss")
            }));
        }

        var data = auths.OrderByDescending(a => a.Created?.UtcTicks ?? 0).Select(a => a.Item);
        return Ok(new ApiResult { Data = data });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var sub = User.FindFirstValue(OpenIddictConstants.Claims.Subject)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        var auth = await authorizationManager.FindByIdAsync(id);
        if (auth == null)
        {
            return Ok(Errors.UserNotExistResult);
        }

        // 只允许删除自己名下的授权
        var authSub = await authorizationManager.GetSubjectAsync(auth);
        if (authSub != sub)
        {
            return Unauthorized(Errors.NotAuthenticated);
        }

        await authorizationManager.DeleteAsync(auth, CancellationToken.None);
        return Ok(ApiResult.Ok("删除成功"));
    }
}
