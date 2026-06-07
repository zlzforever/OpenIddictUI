using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Hybrid;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddictUI.Identity;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace OpenIddictUI.Controllers;

/// <summary>
/// OAuth Consent 控制器 — 授权同意页的数据 API
/// 流程：Authorize() 把请求数据存入 HybridCache → 传 consentId 给前端
/// → 前端 GET /api/consent/{id} 获取客户端信息展示 → 用户点同意
/// → POST /api/consent/{id} 创建永久授权 → 302 回到 /connect/authorize 签发 code
/// </summary>
[Authorize]
[Route("api/consent")]
public class ConsentController(
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictAuthorizationManager authorizationManager,
    HybridCache cache,
    SignInManager<User> signInManager,
    UserManager<User> userManager,
    ILogger<ConsentController> logger) : Controller
{
    /// <summary>
    /// GET — 返回 consent 页面需要的客户端信息 + scope 列表
    /// consentId 是 AuthorizationController 存储到 HybridCache 的随机 key，前端无法伪造
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Index([FromRoute, StringLength(64)] string id)
    {
        // ① 从 HybridCache 读取原始授权请求（Authorize 步骤存的）
        var entry = await cache.GetOrCreateAsync<ConsentEntry?>(
            id,
            _ => new ValueTask<ConsentEntry?>(default(ConsentEntry?)),
            new HybridCacheEntryOptions { Flags = HybridCacheEntryFlags.DisableLocalCacheWrite });
        if (entry == null)
        {
            logger.LogWarning("Consent: request expired or not found for {Id}", id);
            return BadRequest(Errors.NoConsentRequestMatchingResult);
        }

        // ② 校验客户端存在
        var application = await applicationManager.FindByClientIdAsync(entry.ClientId);
        if (application == null)
        {
            logger.LogWarning("Consent: client {ClientId} not found", entry.ClientId);
            return Ok(Errors.ClientNotFoundResult);
        }

        // ③ 将 scope 分为两类展示：identity scope（openid/profile/email 等）+ resource scope（api1 等）
        var appSettings = await applicationManager.GetSettingsAsync(application);
        var identityScopes = new List<object>();
        var resourceScopes = new List<object>();
        var known = new HashSet<string>
            { Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.Phone, Scopes.Address, Scopes.Roles };

        foreach (var s in entry.Scopes)
        {
            var item = new
            {
                name = s, displayName = ScopeLabel(s), description = (string?)null,
                emphasize = s is Scopes.Profile or Scopes.Email, required = s == Scopes.OpenId, @checked = true
            };
            (known.Contains(s) ? identityScopes : resourceScopes).Add(item);
        }

        // ④ 返回数据：客户端名称、logo、scope 列表、consentId（前端提交时需要回传）
        return Ok(new ApiResult
        {
            Data = new
            {
                id,
                returnUrl = entry.ReturnUrl,
                clientName = await applicationManager.GetDisplayNameAsync(application) ?? entry.ClientId,
                clientUrl = appSettings.GetValueOrDefault("client_url"),
                clientLogoUrl = appSettings.GetValueOrDefault("client_logo_url"),
                allowRememberConsent = true,
                identityScopes,
                resourceScopes
            }
        });
    }

    /// <summary>
    /// POST — 处理用户同意/拒绝
    /// yes：创建永久授权记录（AuthorizationTypes.Permanent）→ 返回原始 callback URL
    /// no：Forbid → OpenIddict 返回 access_denied 到客户端
    /// </summary>
    [HttpPost("{id}")]
    public async Task<IActionResult> Index([FromRoute] string id, [FromBody] ConsentInput model)
    {
        // 拒绝：直接返回 access_denied，OpenIddict 处理回调
        if (model.Button == "no")
        {
            logger.LogInformation("Consent: user denied");
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(
                    new Dictionary<string, string?> { ["error"] = "access_denied" }));
        }

        if (model.Button == "yes")
        {
            // ⑤ 再次从缓存读取（防过期）
            var entry = await cache.GetOrCreateAsync<ConsentEntry?>(
                id,
                _ => new ValueTask<ConsentEntry?>(default(ConsentEntry?)),
                new HybridCacheEntryOptions { Flags = HybridCacheEntryFlags.DisableLocalCacheWrite });
            if (entry == null)
            {
                logger.LogWarning("Consent: request expired during POST for {Id}", id);
                return BadRequest(Errors.NoConsentRequestMatchingResult);
            }

            // ⑥ 安全校验：客户端存在 + 未被禁用
            var application = await applicationManager.FindByClientIdAsync(entry.ClientId);
            if (application == null)
            {
                logger.LogWarning("Consent: client {ClientId} not found", entry.ClientId);
                return BadRequest(Errors.ClientNotFoundResult);
            }

            if (await applicationManager.GetSettingsAsync(application) is { } s
                && string.Equals(s.GetValueOrDefault("enabled"), "false", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Consent: disabled client {ClientId}", entry.ClientId);
                return BadRequest(Errors.ClientDisabled);
            }

            // ⑦ 安全校验：用户选择的 scope 不能超出原始请求的范围（防前端篡改扩权）
            var scopes = model.ScopesConsented ?? [];
            if (scopes.Any(x => !entry.Scopes.Contains(x)))
            {
                logger.LogWarning("Consent: scope manipulation detected for {ClientId}, requested scopes exceed original", entry.ClientId);
                return BadRequest(Errors.ConsentScopesExceed);
            }

            // ⑧ 获取当前用户，创建永久授权记录
            var user = await userManager.GetUserAsync(User);
            if (user == null)
            {
                logger.LogError("Consent: authenticated user not found");
                return BadRequest(Errors.UserNotExistResult);
            }

            var principal = await signInManager.CreateUserPrincipalAsync(user);
            principal.SetScopes(scopes);
            // 注意：client 参数必须用 GetIdAsync（内部 ID），不是 ClientId 字符串
            await authorizationManager.CreateAsync(principal, await userManager.GetUserIdAsync(user),
                (await applicationManager.GetIdAsync(application))!,
                AuthorizationTypes.Permanent, [.. scopes]);

            logger.LogInformation("Consent: granted for {ClientId}, scopes={Scopes}", entry.ClientId, string.Join(" ", scopes));

            // ⑨ 返回原始 authorize URL → 前端 window.location 跳转回去 → Authorize() 检测到已有授权 → 签发 code
            return Ok(ApiResult.Ok(data: new { location = entry.ReturnUrl }));
        }

        return BadRequest(Errors.ConsentInvalid);
    }

    private static string ScopeLabel(string s) => s switch
    {
        Scopes.OpenId => "用户标识", Scopes.Profile => "用户数据", Scopes.Email => "邮箱",
        Scopes.Phone => "手机号", Scopes.Address => "地址", Scopes.Roles => "角色",
        _ => s
    };
}

/// <summary>
/// HybridCache 中存储的 consent 请求数据（Authorize 步骤写入）
/// </summary>
public class ConsentEntry
{
    public string ClientId { get; init; } = string.Empty;
    public string ReturnUrl { get; init; } = string.Empty;
    public HashSet<string> Scopes { get; init; } = [];
}

public class ConsentInput
{
    public string[]? ScopesConsented { get; set; }
    [StringLength(10)]
    public string? Button { get; set; }
}
