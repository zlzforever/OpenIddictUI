using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace OpenIddictUI.Grants;

public class AuthorizationCodeGrantHandler : BaseGrantHandler
{
    public const string GrantType = "authorization_code";

    protected override async Task<GrantResult> HandleAsync(OpenIddictRequest request, HttpContext context,
        CancellationToken cancellationToken)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<AuthorizationCodeGrantHandler>>();
        var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        if (result.Principal == null)
        {
            logger.LogWarning("Code/refresh grant: no principal");
            return Failure("The authorization code or refresh token is invalid");
        }

        logger.LogInformation("Code/refresh grant: success");
        return Success(result.Principal);
    }
}