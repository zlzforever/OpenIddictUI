using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
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

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        if (!IsAdmin()) return Unauthorized(Errors.NotAuthenticated);
        var app = await applicationManager.FindByIdAsync(id);
        if (app == null) return Ok(Errors.UserNotExistResult);

        var permissions = (await applicationManager.GetPermissionsAsync(app)).ToList();
        var requirements = await applicationManager.GetRequirementsAsync(app);
        var settings = await applicationManager.GetSettingsAsync(app);

        return Ok(new ApiResult
        {
            Data = new
            {
                id = await applicationManager.GetIdAsync(app),
                clientId = await applicationManager.GetClientIdAsync(app),
                displayName = await applicationManager.GetDisplayNameAsync(app),
                clientType = await applicationManager.GetClientTypeAsync(app) ?? "confidential",
                applicationType = await applicationManager.GetApplicationTypeAsync(app) ?? "web",
                consentType = await applicationManager.GetConsentTypeAsync(app) ?? "implicit",
                redirectUris = await applicationManager.GetRedirectUrisAsync(app),
                postLogoutRedirectUris = await applicationManager.GetPostLogoutRedirectUrisAsync(app),
                grantTypes = permissions.Where(p => p.StartsWith("gt:")).Select(p => p[3..]).ToList(),
                scopes = permissions.Where(p => p.StartsWith("scp:")).Select(p => p[4..]).ToList(),
                clientUrl = settings.GetValueOrDefault("client_url"),
                clientLogoUrl = settings.GetValueOrDefault("client_logo_url"),
                enabled = settings.GetValueOrDefault("enabled") ?? "true",
                requirePkce = requirements.Contains(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange),
                accessTokenLifetime = GetLifetimeSeconds(settings, OpenIddictConstants.Settings.TokenLifetimes.AccessToken),
                authorizationCodeLifetime = GetLifetimeSeconds(settings, OpenIddictConstants.Settings.TokenLifetimes.AuthorizationCode),
                refreshTokenLifetime = GetLifetimeSeconds(settings, OpenIddictConstants.Settings.TokenLifetimes.RefreshToken),
                identityTokenLifetime = GetLifetimeSeconds(settings, OpenIddictConstants.Settings.TokenLifetimes.IdentityToken),
                deviceCodeLifetime = GetLifetimeSeconds(settings, OpenIddictConstants.Settings.TokenLifetimes.DeviceCode),
                userCodeLifetime = GetLifetimeSeconds(settings, OpenIddictConstants.Settings.TokenLifetimes.UserCode)
            }
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ApplicationInput input)
    {
        if (!IsAdmin()) return Unauthorized(Errors.NotAuthenticated);
        var err = ValidateApplicationInput(input, isUpdate: false);
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
        var err = ValidateApplicationInput(input, isUpdate: true);
        if (err != null) return Ok(err);

        var app = await applicationManager.FindByIdAsync(id);
        if (app == null) return Ok(Errors.UserNotExistResult);

        err = ValidateApplicationInput(
            input,
            isUpdate: true,
            existingClientType: await applicationManager.GetClientTypeAsync(app));
        if (err != null) return Ok(err);

        var descriptor = new OpenIddictApplicationDescriptor();
        await applicationManager.PopulateAsync(descriptor, app, CancellationToken.None);
        BuildDescriptor(input, descriptor);

        await applicationManager.UpdateAsync(app, descriptor, CancellationToken.None);
        return Ok(ApiResult.Ok("更新成功"));
    }

    private static ApiResult? ValidateApplicationInput(
        ApplicationInput input, bool isUpdate, string? existingClientType = null)
    {
        var err = (int code, string msg) => ApiResult.Error(code, msg);
        var clientType = input.ClientType ?? "confidential";

        if (string.Equals(clientType, "public", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(input.ClientSecret))
            return err(Errors.InvalidRequest.Code, "public 客户端不能设置 ClientSecret");

        var requiresNewCredentials = !isUpdate ||
            !string.Equals(existingClientType ?? "confidential", clientType, StringComparison.OrdinalIgnoreCase);
        if (string.Equals(clientType, "confidential", StringComparison.OrdinalIgnoreCase) &&
            requiresNewCredentials && string.IsNullOrWhiteSpace(input.ClientSecret) &&
            string.IsNullOrWhiteSpace(input.JsonWebKeySet))
            return err(Errors.InvalidRequest.Code, "confidential 客户端必须设置 ClientSecret 或 JWKS");

        if (input.GrantTypes?.Contains("authorization_code") == true &&
            (input.RedirectUris == null || input.RedirectUris.Count == 0))
            return err(Errors.InvalidRequest.Code, "authorization_code grant 必须设置 RedirectUris");

        if (input.AccessTokenLifetime is <= 0) return err(Errors.InvalidRequest.Code, "AccessTokenLifetime 必须大于 0");
        if (input.AuthorizationCodeLifetime is <= 0)
            return err(Errors.InvalidRequest.Code, "AuthorizationCodeLifetime 必须大于 0");
        if (input.RefreshTokenLifetime is <= 0) return err(Errors.InvalidRequest.Code, "RefreshTokenLifetime 必须大于 0");
        if (input.IdentityTokenLifetime is <= 0) return err(Errors.InvalidRequest.Code, "IdentityTokenLifetime 必须大于 0");
        if (input.DeviceCodeLifetime is <= 0) return err(Errors.InvalidRequest.Code, "DeviceCodeLifetime 必须大于 0");
        if (input.UserCodeLifetime is <= 0) return err(Errors.InvalidRequest.Code, "UserCodeLifetime 必须大于 0");

        return null;
    }

    private static OpenIddictApplicationDescriptor BuildDescriptor(
        ApplicationInput input, OpenIddictApplicationDescriptor? descriptor = null)
    {
        var isPublic = string.Equals(input.ClientType, "public", StringComparison.OrdinalIgnoreCase);
        descriptor ??= new OpenIddictApplicationDescriptor();
        descriptor.ClientId = input.ClientId;
        descriptor.ClientType = input.ClientType ?? "confidential";
        descriptor.ConsentType = input.ConsentType ?? "implicit";
        descriptor.DisplayName = input.DisplayName;
        descriptor.ApplicationType = input.ApplicationType ?? "web";

        if (isPublic)
        {
            descriptor.ClientSecret = null;
        }
        else if (input.ClientSecret is not null)
        {
            descriptor.ClientSecret = input.ClientSecret;
        }

        if (input.JsonWebKeySet is not null)
        {
            descriptor.JsonWebKeySet = string.IsNullOrWhiteSpace(input.JsonWebKeySet)
                ? null
                : new JsonWebKeySet(input.JsonWebKeySet);
        }

        descriptor.Settings.Remove("client_url");
        if (!string.IsNullOrEmpty(input.ClientUrl)) descriptor.Settings["client_url"] = input.ClientUrl;
        descriptor.Settings.Remove("client_logo_url");
        if (!string.IsNullOrEmpty(input.ClientLogoUrl)) descriptor.Settings["client_logo_url"] = input.ClientLogoUrl;
        descriptor.Settings["enabled"] = input.Enabled ? "true" : "false";

        var preservedPermissions = descriptor.Permissions
            .Where(p => !p.StartsWith("scp:") && !p.StartsWith("gt:"))
            .ToList();
        descriptor.Permissions.Clear();
        foreach (var permission in preservedPermissions)
            descriptor.Permissions.Add(permission);

        descriptor.RedirectUris.Clear();
        foreach (var u in input.RedirectUris ?? []) descriptor.RedirectUris.Add(new Uri(u));

        descriptor.PostLogoutRedirectUris.Clear();
        foreach (var u in input.PostLogoutRedirectUris ?? []) descriptor.PostLogoutRedirectUris.Add(new Uri(u));

        // grant types → permissions
        foreach (var gt in input.GrantTypes ?? [])
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.GrantType + gt);

        // authorization_code → need response_type=code
        if (input.GrantTypes?.Contains("authorization_code") ?? false)
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Code);

        // scopes
        foreach (var sc in input.Scopes ?? [])
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + sc);

        // 有 redirect_uri → add endpoint permissions
        if (input.RedirectUris is { Count: > 0 })
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
        }

        if (input.PostLogoutRedirectUris is { Count: > 0 })
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.EndSession);

        // public 客户端强制 PKCE
        descriptor.Requirements.Clear();
        if (isPublic || input.RequirePkce)
            descriptor.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);

        // Token lifetimes (seconds → TimeSpan)
        descriptor.SetAccessTokenLifetime(ToTimeSpan(input.AccessTokenLifetime));
        descriptor.SetAuthorizationCodeLifetime(ToTimeSpan(input.AuthorizationCodeLifetime));
        descriptor.SetRefreshTokenLifetime(ToTimeSpan(input.RefreshTokenLifetime));
        descriptor.SetIdentityTokenLifetime(ToTimeSpan(input.IdentityTokenLifetime));
        descriptor.SetDeviceCodeLifetime(ToTimeSpan(input.DeviceCodeLifetime));
        descriptor.SetUserCodeLifetime(ToTimeSpan(input.UserCodeLifetime));

        return descriptor;
    }

    private static TimeSpan? ToTimeSpan(int? seconds)
        => seconds.HasValue ? TimeSpan.FromSeconds(seconds.Value) : null;

    private static int? GetLifetimeSeconds(IReadOnlyDictionary<string, string> settings, string key)
    {
        if (!settings.TryGetValue(key, out var value) ||
            !TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var lifetime))
            return null;

        return (int)lifetime.TotalSeconds;
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
