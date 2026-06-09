using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddictUI.Grants;

namespace OpenIddictUI.Controllers;

[Authorize]
[Route("api/applications")]
public class ApplicationsController(
    IOpenIddictApplicationManager applicationManager,
    IEnumerable<IGrantHandler> grantHandlers) : Controller
{
    [HttpGet("grant-types")]
    public IActionResult GetGrantTypes()
        => Ok(new ApiResult
        {
            Data = grantHandlers.Select(handlerType => handlerType.GetType()
                .GetField("GrantType",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                ?.GetValue(null) as string ?? string.Empty).Distinct().Order()
        });

    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (!IsAdmin()) return Unauthorized(Errors.NotAuthenticated);
        var apps = new List<object>();
        await foreach (var app in applicationManager.ListAsync())
        {
            var perms = (await applicationManager.GetPermissionsAsync(app)).ToList();
            var settings = await applicationManager.GetSettingsAsync(app);
            apps.Add(new
            {
                id = await applicationManager.GetIdAsync(app),
                clientId = await applicationManager.GetClientIdAsync(app),
                displayName = await applicationManager.GetDisplayNameAsync(app),
                clientType = await applicationManager.GetClientTypeAsync(app) ?? "confidential",
                applicationType = await applicationManager.GetApplicationTypeAsync(app) ?? "web",
                consentType = await applicationManager.GetConsentTypeAsync(app) ?? "implicit",
                redirectUris = await applicationManager.GetRedirectUrisAsync(app),
                postLogoutRedirectUris = await applicationManager.GetPostLogoutRedirectUrisAsync(app),
                grantTypes = perms.Where(p => p.StartsWith("gt:")).Select(p => p[3..]).ToList(),
                scopes = perms.Where(p => p.StartsWith("scp:")).Select(p => p[4..]).ToList(),
                clientUrl = settings?.GetValueOrDefault("client_url"),
                clientLogoUrl = settings?.GetValueOrDefault("client_logo_url"),
                enabled = settings?.GetValueOrDefault("enabled") ?? "true"
            });
        }

        return Ok(new ApiResult { Data = apps });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ApplicationInput input)
    {
        if (!IsAdmin()) return Unauthorized(Errors.NotAuthenticated);
        var err = ValidateApplicationInput(input);
        if (err != null) return Ok(err);
        if (await applicationManager.FindByClientIdAsync(input.ClientId) != null)
            return Ok(Errors.InvalidRequest);

        await applicationManager.CreateAsync(BuildDescriptor(input), CancellationToken.None);
        return Ok(ApiResult.Ok("创建成功"));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] ApplicationInput input)
    {
        if (!IsAdmin()) return Unauthorized(Errors.NotAuthenticated);
        var err = ValidateApplicationInput(input);
        if (err != null) return Ok(err);
        var app = await applicationManager.FindByIdAsync(id);
        if (app == null) return Ok(Errors.UserNotExistResult);

        var descriptor = BuildDescriptor(input);
        foreach (var p in await applicationManager.GetPermissionsAsync(app))
            if (!p.StartsWith("scp:") && !p.StartsWith("gt:"))
                descriptor.Permissions.Add(p);

        await applicationManager.UpdateAsync(app, descriptor, CancellationToken.None);
        return Ok(ApiResult.Ok("更新成功"));
    }

    private static ApiResult? ValidateApplicationInput(ApplicationInput input)
    {
        var err = (int code, string msg) => ApiResult.Error(code, msg);

        if (input.ClientType == "public" && !string.IsNullOrEmpty(input.ClientSecret))
            return err(Errors.InvalidRequest.Code, "public 客户端不能设置 ClientSecret");

        if (input.ClientType == "confidential" && string.IsNullOrEmpty(input.ClientSecret) && string.IsNullOrEmpty(input.JsonWebKeySet))
            return err(Errors.InvalidRequest.Code, "confidential 客户端必须设置 ClientSecret 或 JWKS");

        if (input.GrantTypes?.Contains("authorization_code") == true && (input.RedirectUris == null || input.RedirectUris.Count == 0))
            return err(Errors.InvalidRequest.Code, "authorization_code grant 必须设置 RedirectUris");

        if (input.AccessTokenLifetime is <= 0) return err(Errors.InvalidRequest.Code, "AccessTokenLifetime 必须大于 0");
        if (input.AuthorizationCodeLifetime is <= 0) return err(Errors.InvalidRequest.Code, "AuthorizationCodeLifetime 必须大于 0");
        if (input.RefreshTokenLifetime is <= 0) return err(Errors.InvalidRequest.Code, "RefreshTokenLifetime 必须大于 0");
        if (input.IdentityTokenLifetime is <= 0) return err(Errors.InvalidRequest.Code, "IdentityTokenLifetime 必须大于 0");
        if (input.DeviceCodeLifetime is <= 0) return err(Errors.InvalidRequest.Code, "DeviceCodeLifetime 必须大于 0");
        if (input.UserCodeLifetime is <= 0) return err(Errors.InvalidRequest.Code, "UserCodeLifetime 必须大于 0");

        return null;
    }

    private static OpenIddictApplicationDescriptor BuildDescriptor(ApplicationInput input)
    {
        var isPublic = string.Equals(input.ClientType, "public", StringComparison.OrdinalIgnoreCase);
        var d = new OpenIddictApplicationDescriptor
        {
            ClientId = input.ClientId,
            ClientSecret = isPublic ? null : input.ClientSecret,
            ClientType = input.ClientType ?? "confidential",
            ConsentType = input.ConsentType ?? "implicit",
            DisplayName = input.DisplayName,
            ApplicationType = input.ApplicationType ?? "web"
        };

        if (!string.IsNullOrEmpty(input.ClientUrl)) d.Settings["client_url"] = input.ClientUrl;
        if (!string.IsNullOrEmpty(input.ClientLogoUrl)) d.Settings["client_logo_url"] = input.ClientLogoUrl;
        if (!string.IsNullOrEmpty(input.JsonWebKeySet)) d.Settings["jwks_json"] = input.JsonWebKeySet;
        d.Settings["enabled"] = input.Enabled ? "true" : "false";

        foreach (var u in input.RedirectUris ?? []) d.RedirectUris.Add(new Uri(u));
        foreach (var u in input.PostLogoutRedirectUris ?? []) d.PostLogoutRedirectUris.Add(new Uri(u));

        // grant types → permissions
        foreach (var gt in input.GrantTypes ?? [])
            d.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.GrantType + gt);

        // authorization_code → need response_type=code
        if ((input.GrantTypes?.Contains("authorization_code") ?? false))
            d.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Code);

        // scopes
        foreach (var sc in input.Scopes ?? [])
            d.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + sc);

        // 有 redirect_uri → add endpoint permissions
        if (input.RedirectUris is { Count: > 0 })
        {
            d.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
            d.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
        }

        if (input.PostLogoutRedirectUris is { Count: > 0 })
            d.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.EndSession);

        // public 客户端强制 PKCE
        if (isPublic || input.RequirePkce)
            d.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);

        // Token lifetimes (seconds → TimeSpan)
        if (input.AccessTokenLifetime.HasValue) d.SetAccessTokenLifetime(TimeSpan.FromSeconds(input.AccessTokenLifetime.Value));
        if (input.AuthorizationCodeLifetime.HasValue) d.SetAuthorizationCodeLifetime(TimeSpan.FromSeconds(input.AuthorizationCodeLifetime.Value));
        if (input.RefreshTokenLifetime.HasValue) d.SetRefreshTokenLifetime(TimeSpan.FromSeconds(input.RefreshTokenLifetime.Value));
        if (input.IdentityTokenLifetime.HasValue) d.SetIdentityTokenLifetime(TimeSpan.FromSeconds(input.IdentityTokenLifetime.Value));
        if (input.DeviceCodeLifetime.HasValue) d.SetDeviceCodeLifetime(TimeSpan.FromSeconds(input.DeviceCodeLifetime.Value));
        if (input.UserCodeLifetime.HasValue) d.SetUserCodeLifetime(TimeSpan.FromSeconds(input.UserCodeLifetime.Value));

        return d;
    }

    private bool IsAdmin() => User.Identity?.Name == "admin";
}

public class ApplicationInput
{
    [Required, StringLength(100)] public string ClientId { get; set; } = string.Empty;
    [StringLength(512)] public string? ClientSecret { get; set; }
    [StringLength(200)] public string? DisplayName { get; set; }
    [StringLength(20)] public string? ClientType { get; set; }
    [StringLength(20)] public string? ConsentType { get; set; }
    public List<string>? RedirectUris { get; set; }
    public List<string>? PostLogoutRedirectUris { get; set; }
    public List<string>? GrantTypes { get; set; }
    public List<string>? Scopes { get; set; }
    [StringLength(512)] public string? ClientUrl { get; set; }
    [StringLength(512)] public string? ClientLogoUrl { get; set; }
    [StringLength(20)] public string? ApplicationType { get; set; }
    public string? JsonWebKeySet { get; set; }
    public int? AccessTokenLifetime { get; set; }
    public int? AuthorizationCodeLifetime { get; set; }
    public int? RefreshTokenLifetime { get; set; }
    public int? IdentityTokenLifetime { get; set; }
    public int? DeviceCodeLifetime { get; set; }
    public int? UserCodeLifetime { get; set; }
    public bool RequirePkce { get; set; }
    public bool Enabled { get; set; } = true;
}