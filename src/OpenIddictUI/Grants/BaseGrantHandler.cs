using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace OpenIddictUI.Grants;

public abstract class BaseGrantHandler : IGrantHandler
{
    // 子类实现具体校验逻辑 → 返回 GrantResult（Success/Failure）
    protected abstract Task<GrantResult> HandleAsync(OpenIddictRequest request, HttpContext context,
        CancellationToken cancellationToken);

    // 将 GrantResult 转为 MVC IActionResult：Success → SignInResult, Failure → ForbidResult
    public async Task<IActionResult> ExecuteAsync(OpenIddictRequest request, HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await HandleAsync(request, context, cancellationToken);
        var logger = context.RequestServices.GetService<ILogger<BaseGrantHandler>>();

        if (result.IsError)
        {
            logger?.LogWarning("Grant handler {Handler}: failed — {Error}: {Description}",
                GetType().Name, result.Error ?? "unknown", result.ErrorDescription ?? "no description");

            return new ForbidResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = result.Error ?? "invalid_grant",
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = result.ErrorDescription
                }));
        }

        logger?.LogDebug("Grant handler {Handler}: success", GetType().Name);

        return new SignInResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, result.Principal!);
    }

    protected static GrantResult Success(ClaimsPrincipal principal) => GrantResult.Success(principal);

    protected static GrantResult Failure(string description) =>
        GrantResult.Failure("invalid_grant", description);

    protected static IList<string> GetDestinations(Claim claim) => claim.Type switch
    {
        Claims.Name or Claims.PreferredUsername or Claims.Email or Claims.PhoneNumber
            => [Destinations.AccessToken, Destinations.IdentityToken],
        Claims.Role => [Destinations.AccessToken, Destinations.IdentityToken],
        _ => [Destinations.AccessToken]
    };
}