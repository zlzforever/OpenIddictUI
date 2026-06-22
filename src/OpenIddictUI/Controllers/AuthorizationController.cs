using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Hybrid;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddictUI.Grants;
using OpenIddictUI.Identity;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace OpenIddictUI.Controllers;

/// <summary>
/// OpenID Connect 授权端点 — 处理 /connect/authorize 和 /connect/token
/// </summary>
[Route("connect")]
public class AuthorizationController(
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictAuthorizationManager authorizationManager,
    IOpenIddictScopeManager scopeManager,
    SignInManager<User> signInManager,
    UserManager<User> userManager,
    HybridCache cache,
    ILogger<AuthorizationController> logger) : Controller
{
    /// <summary>
    /// OAuth 2.0 / OpenID Connect 授权端点
    /// 客户端（如 SPA）将用户重定向到此处，附带 response_type=code、client_id、scope、redirect_uri 等参数
    /// </summary>
    [HttpGet("authorize")]
    [HttpPost("authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize()
    {
        // ① 从当前 HTTP 上下文中获取 OpenIddict 解析好的授权请求参数
        var request = HttpContext.GetOpenIddictServerRequest();
        if (request == null)
        {
            logger.LogWarning("Authorize: no OpenIddict request found");
            return BadRequest(new
                { error = "invalid_request", error_description = "The OpenID Connect request cannot be retrieved." });
        }

        // ② 验证客户端：根据 client_id 查找已注册的应用程序
        if (string.IsNullOrEmpty(request.ClientId))
        {
            logger.LogWarning("Authorize: missing client_id");
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = "invalid_request",
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                        "The client_id parameter is missing"
                }));
        }

        var application = await applicationManager.FindByClientIdAsync(request.ClientId);
        if (application == null)
        {
            logger.LogWarning("Authorize: unknown client {ClientId}", request.ClientId);
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = "invalid_client",
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Application not found"
                }));
        }

        // ②b 验证客户端启用状态：通过自定义 Settings["enabled"] 判断是否被禁用
        if (await applicationManager.GetSettingsAsync(application) is { } settings
            && string.Equals(settings.GetValueOrDefault("enabled"), "false", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Authorize: disabled client {ClientId}", request.ClientId);
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = "invalid_client",
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Application is disabled"
                }));
        }

        // ③ 验证用户认证状态 — 使用默认认证方案（通常是 Identity.Application cookie）
        var result = await HttpContext.AuthenticateAsync();
        if (!result.Succeeded)
        {
            // prompt=none 表示客户端要求静默授权：如果用户未登录，不应跳转登录页，直接返回错误
            if (request.HasPromptValue(PromptValues.None))
            {
                logger.LogInformation("Authorize: prompt=none but user not authenticated");
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = "login_required",
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Authentication is required"
                    }));
            }

            return RedirectToLogin(GetRequestUri());
        }

        // ④ 从认证结果中获取当前用户实体
        var user = await userManager.GetUserAsync(result.Principal);
        if (user == null)
        {
            logger.LogError("Authorize: authenticated user not found");
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = "invalid_grant",
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "User not found"
                }));
        }

        // ⑤ 检查 max_age 参数：如果客户端要求认证时长不超过 X 秒，而当前会话已超时
        if (request.MaxAge != null && result.Properties?.IssuedUtc != null &&
            DateTimeOffset.UtcNow - result.Properties.IssuedUtc > TimeSpan.FromSeconds(request.MaxAge.Value))
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = "login_required",
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "User session expired"
                    }));
            }

            return RedirectToLogin(GetRequestUri());
        }

        // ⑥ 处理 prompt=login — 强制重新认证：先清除当前会话，再跳转登录页
        if (request.HasPromptValue(PromptValues.Login))
        {
            await HttpContext.SignOutAsync();
            return RedirectToLogin(GetRequestUri());
        }

        // ⑦ 检查授权同意（Consent）类型
        //  - implicit/external：无需用户手动同意，直接签发 code
        //  - explicit：需用户显式批准，检查是否已有永久授权记录
        var appConsentType = await applicationManager.GetConsentTypeAsync(application) ?? ConsentTypes.Implicit;
        var hasExistingAuth = false;

        if (appConsentType != ConsentTypes.Explicit)
        {
            hasExistingAuth = true;
        }
        else
        {
            var existingAuths = authorizationManager.FindAsync(
                subject: await userManager.GetUserIdAsync(user),
                client: (await applicationManager.GetIdAsync(application))!,
                status: Statuses.Valid,
                type: AuthorizationTypes.Permanent,
                scopes: request.GetScopes());

            await foreach (var _ in existingAuths)
            {
                hasExistingAuth = true;
                break;
            }
        }

        if (!hasExistingAuth)
        {
            logger.LogInformation("Authorize: {ClientId} requires explicit consent, redirecting", request.ClientId);
            var consentId = Guid.NewGuid().ToString("N");
            var entry = new ConsentEntry
                { ClientId = request.ClientId, ReturnUrl = GetRequestUri(), Scopes = request.GetScopes().ToHashSet() };
            await cache.SetAsync(consentId, entry,
                new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(10) });
            return Redirect($"/consent/{consentId}");
        }

        // ⑧ 构建 ClaimsPrincipal，签发 authorization_code
        var principal = await signInManager.CreateUserPrincipalAsync(user);
        principal.RemoveClaims(ClaimTypes.NameIdentifier);
        principal.SetClaim(Claims.Subject, await userManager.GetUserIdAsync(user));
        principal.SetScopes(request.GetScopes());
        principal.SetResources(await scopeManager.ListResourcesAsync(principal.GetScopes()).ToListAsync());

        // SetDestinations 决定每个 claim 出现在哪种 token 中（AccessToken / IdentityToken / 两者）
        foreach (var c in principal.Claims)
        {
            c.SetDestinations(GetDestinations(c));
        }

        logger.LogInformation("Authorize: success for {ClientId}, scopes={Scopes}", request.ClientId,
            string.Join(" ", request.GetScopes()));
        // OpenIddict 自动生成 authorization_code，302 跳转到客户端回调地址
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    // /// <summary>
    // /// OIDC RP-Initiated Logout — 接收 oidc-client-ts signoutRedirect() 回调
    // /// 流程：
    // ///   ① 收到 GET /connect/logout?id_token_hint=xxx&post_logout_redirect_uri=http://localhost:5175
    // ///   ② OpenIddict 验证 id_token_hint + post_logout_redirect_uri
    // ///   ③ 清除 Identity cookie（本地会话）
    // ///   ④ 返回 302 重定向到 post_logout_redirect_uri（SPA 登出页）
    [HttpGet("logout")]
    [HttpPost("logout")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Logout()
    {
        logger.LogInformation("Logout: RP-Initiated logout");

        // 先调 OpenIddict SignOut → 验证 post_logout_redirect_uri 并生成重定向响应
        var result = await HttpContext.AuthenticateAsync();
        if (!result.Succeeded)
        {
            logger.LogWarning("Logout: user not authenticated, returning default signout");
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return SignOut(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        // 清除本地 Identity cookie
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);

        // OpenIddict 根据 id_token_hint + post_logout_redirect_uri 生成重定向响应
        return SignOut(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Token 端点 — 用 authorization_code 换 access_token，或 password/phone_code 等 grant 直接获取 token
    /// </summary>
    [HttpPost("token")]
    [Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest();
        if (request == null)
        {
            return BadRequest(new { error = "invalid_request" });
        }

        var grantType = request.GrantType;
        if (grantType == null)
        {
            return BadRequest(new { error = "unsupported_grant_type" });
        }

        // refresh_token 和 authorization_code 共用一个 handler
        if (request.IsRefreshTokenGrantType())
        {
            grantType = "authorization_code";
        }

        var handler = HttpContext.RequestServices.GetKeyedService<IGrantHandler>(grantType);
        if (handler == null)
        {
            logger.LogWarning("Exchange: no handler for grant type {GrantType}", grantType);
            return BadRequest(new { error = "unsupported_grant_type" });
        }

        return await handler.ExecuteAsync(request, HttpContext, CancellationToken.None);
    }

    [HttpGet("userinfo")]
    public async Task GetUserInfo()
    {
        var authenticateResult = await HttpContext.AuthenticateAsync();
        if (authenticateResult.Succeeded == false)
        {
            logger.LogWarning("Authorize: unsuccessful authentication, {Message}", authenticateResult.Failure?.Message);
            HttpContext.Response.StatusCode = 401;
            return;
        }

        var dictionary = new Dictionary<string, object>();
        foreach (var claim in HttpContext.User.Claims)
        {
            // 优先使用映射后的 JWT 短名称，没有映射就用原始名称
            var key = Util.JwtClaimMappings.TryGetValue(claim.Type, out var jwtKey) ? jwtKey : claim.Type;

            // 处理多值 Claim（如多个角色）
            if (dictionary.TryGetValue(key, out var existing))
            {
                if (existing is List<object> list)
                {
                    list.Add(claim.Value);
                }
                else
                {
                    dictionary[key] = new List<object> { existing, claim.Value };
                }
            }
            else
            {
                dictionary[key] = claim.Value;
            }
        }

        HttpContext.Response.ContentType = "application/json";
        await HttpContext.Response.WriteAsJsonAsync(dictionary);
    }

    /// <summary>
    /// 获取当前请求的完整 URI（用于 returnUrl 参数）
    /// </summary>
    private string GetRequestUri()
    {
        var path = Request.PathBase + Request.Path + Request.QueryString;
        var issuer = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["OpenIddictUI:Issuer"];
        return !string.IsNullOrEmpty(issuer) ? $"{issuer.TrimEnd('/')}{path}" : path;
    }

    private RedirectResult RedirectToLogin(string returnUrl)
    {
        var issuer = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["OpenIddictUI:Issuer"];
        var loginUrl = string.IsNullOrEmpty(issuer)
            ? "/account/login"
            : $"{issuer.TrimEnd('/')}/account/login";
        return Redirect($"{loginUrl}?returnUrl={Uri.EscapeDataString(returnUrl)}");
    }

    /// <summary>
    /// 决定每个 claim 的目标 token 类型
    /// AccessToken：用于 API 授权
    /// IdentityToken：用于客户端获取用户身份信息
    /// </summary>
    private static IList<string> GetDestinations(Claim claim) => claim.Type switch
    {
        Claims.Name or Claims.PreferredUsername or Claims.Email or Claims.PhoneNumber
            => [Destinations.AccessToken, Destinations.IdentityToken],
        Claims.Role => [Destinations.AccessToken, Destinations.IdentityToken],
        _ => [Destinations.AccessToken]
    };
}