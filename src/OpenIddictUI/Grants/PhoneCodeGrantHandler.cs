using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddictUI.Identity;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace OpenIddictUI.Grants;

public class PhoneCodeGrantHandler : BaseGrantHandler
{
    public const string GrantType = "phone_code";

    protected override async Task<GrantResult> HandleAsync(OpenIddictRequest request, HttpContext context,
        CancellationToken cancellationToken)
    {
        var services = context.RequestServices;
        var userManager = services.GetRequiredService<UserManager<User>>();
        var signInManager = services.GetRequiredService<SignInManager<User>>();
        var scopeManager = services.GetRequiredService<IOpenIddictScopeManager>();
        var logger = services.GetRequiredService<ILogger<PhoneCodeGrantHandler>>();

        var phoneNumber = (string?)request["phone_number"];
        var code = (string?)request["code"];

        if (string.IsNullOrEmpty(phoneNumber) || string.IsNullOrEmpty(code))
        {
            return Failure("手机号或验证码不能为空");
        }

        var user = await userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber, cancellationToken);
        if (user == null)
        {
            logger.LogWarning("Phone grant: user {Phone} not found", phoneNumber);
            return Failure("用户不存在");
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return Failure("用户被锁定");
        }

        var isValid = await userManager.VerifyUserTokenAsync(user, TokenOptions.DefaultPhoneProvider, "Login", code);
        if (!isValid)
        {
            logger.LogWarning("Phone grant: invalid code for {Phone}", phoneNumber);

            if (userManager.SupportsUserLockout)
            {
                var afResult = await userManager.AccessFailedAsync(user);
                if (!afResult.Succeeded)
                {
                    logger.LogWarning("Phone grant: AccessFailed 失败 {Phone}, {Errors}", phoneNumber,
                        string.Join(", ", afResult.Errors.Select(e => e.Description)));
                }
            }

            return Failure("验证码不正确");
        }

        // 验证码正确 → 重置锁定计数（与 CheckPasswordSignInAsync 内 alwaysLockout 逻辑一致）
        var alwaysLockout =
            AppContext.TryGetSwitch("Microsoft.AspNetCore.Identity.CheckPasswordSignInAlwaysResetLockoutOnSuccess",
                out var resetEnabled) && resetEnabled;
        if (alwaysLockout || !await signInManager.IsTwoFactorEnabledAsync(user) ||
            await signInManager.IsTwoFactorClientRememberedAsync(user))
        {
            var resetLockoutResult = await userManager.ResetAccessFailedCountAsync(user);
            if (!resetLockoutResult.Succeeded)
            {
                logger.LogWarning("Phone grant: ResetAccessFailedCount 失败 {Phone}, {Errors}", phoneNumber,
                    string.Join(", ", resetLockoutResult.Errors.Select(e => e.Description)));
                return Failure("登录失败");
            }
        }

        var principal = await signInManager.CreateUserPrincipalAsync(user);
        principal.SetClaim(Claims.Subject, await userManager.GetUserIdAsync(user));
        principal.SetScopes(request.GetScopes());
        principal.SetResources(await scopeManager
            .ListResourcesAsync(principal.GetScopes(), cancellationToken).
            ToListAsync(cancellationToken: cancellationToken));

        // SetDestinations 决定每个 claim 出现在哪种 token 中（AccessToken / IdentityToken / 两者）
        foreach (var c in principal.Claims)
        {
            c.SetDestinations(GetDestinations(c));
        }
        principal.SetClaim(OpenIddictConstants.Claims.AuthenticationMethodReference, "phone_code");

        logger.LogInformation("Phone grant: success for {Phone}", phoneNumber);
        return Success(principal);
    }
}