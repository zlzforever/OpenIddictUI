using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddictUI.Extensions;
using OpenIddictUI.Identity;
using OpenIddictUI.Options;

namespace OpenIddictUI.Grants;

public class PasswordGrantHandler : BaseGrantHandler
{
    public const string GrantType = "password";

    protected override async Task<GrantResult> HandleAsync(OpenIddictRequest request, HttpContext context,
        CancellationToken cancellationToken)
    {
        var services = context.RequestServices;
        var userManager = services.GetRequiredService<UserManager<User>>();
        var signInManager = services.GetRequiredService<SignInManager<User>>();
        var scopeManager = services.GetRequiredService<IOpenIddictScopeManager>();
        var logger = services.GetRequiredService<ILogger<PasswordGrantHandler>>();
        var serviceOptions = context.RequestServices.GetRequiredService<IOptions<OpenIddictUIOptions>>().Value;
        var hybridCache = services.GetRequiredService<HybridCache>();
        if (string.IsNullOrEmpty(request.Username))
        {
            return Failure("用户名或密码错误");
        }

        var user = await userManager.FindByNameAsync(request.Username);
        if (user == null)
        {
            logger.LogWarning("Password grant: user {Username} not found", request.Username);
            return Failure("用户名或密码错误");
        }

        if (serviceOptions.ForcePasswordGrantVerifyCaptcha)
        {
            var captchaCode = request.GetParameter("CaptchaCode").GetValueOrDefault().GetRawValue()?.ToString();
            if (!await context.CheckCaptchaAsync(hybridCache, captchaCode))
            {
                return Failure("验证码不正确");
            }
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return Failure("用户被锁定");
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password!, true);
        if (!result.Succeeded)
        {
            logger.LogWarning("Password grant: failed for {Username}", request.Username);
            return Failure(result.IsLockedOut ? "用户被锁定"
                : result.IsNotAllowed ? "用户被禁用"
                : "用户名或密码错误");
        }

        var principal = await signInManager.CreateUserPrincipalAsync(user);
        principal.RemoveClaims(ClaimTypes.NameIdentifier);
        principal.SetClaim(OpenIddictConstants.Claims.Subject, await userManager.GetUserIdAsync(user));
        principal.SetScopes(request.GetScopes());
        principal.SetResources(await scopeManager
            .ListResourcesAsync(principal.GetScopes(), cancellationToken).
            ToListAsync(cancellationToken: cancellationToken));

        // SetDestinations 决定每个 claim 出现在哪种 token 中（AccessToken / IdentityToken / 两者）
        foreach (var c in principal.Claims)
        {
            c.SetDestinations(GetDestinations(c));
        }
        principal.SetClaim(OpenIddictConstants.Claims.AuthenticationMethodReference, "password");

        logger.LogInformation("Password grant: success for {Username}", request.Username);
        return Success(principal);
    }
}