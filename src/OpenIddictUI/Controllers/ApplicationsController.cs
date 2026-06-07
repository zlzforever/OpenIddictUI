using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;

namespace OpenIddictUI.Controllers;

[Authorize]
[Route("api/applications")]
public class ApplicationsController(IOpenIddictApplicationManager applicationManager) : Controller
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (!IsAdmin())
        {
            return Unauthorized(Errors.NotAuthenticated);
        }
        var apps = new List<object>();
        await foreach (var app in applicationManager.ListAsync())
        {
            var perms = (await applicationManager.GetPermissionsAsync(app)).ToList();
            apps.Add(new
            {
                id = await applicationManager.GetIdAsync(app),
                clientId = await applicationManager.GetClientIdAsync(app),
                displayName = await applicationManager.GetDisplayNameAsync(app),
                clientType = await applicationManager.GetClientTypeAsync(app) ?? "confidential",
                consentType = await applicationManager.GetConsentTypeAsync(app) ?? "implicit",
                redirectUris = await applicationManager.GetRedirectUrisAsync(app),
                postLogoutRedirectUris = await applicationManager.GetPostLogoutRedirectUrisAsync(app),
                scopes = perms.Where(p => p.StartsWith("scp:")).Select(p => p[4..]).ToList(),
                enabled = (await applicationManager.GetSettingsAsync(app))?.GetValueOrDefault("enabled") ?? "true"
            });
        }

        return Ok(new ApiResult { Data = apps });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ApplicationInput input)
    {
        if (!IsAdmin())
        {
            return Unauthorized(Errors.NotAuthenticated);
        }
        if (await applicationManager.FindByClientIdAsync(input.ClientId) != null)
        {
            return Ok(Errors.InvalidRequest);
        }

        await applicationManager.CreateAsync(BuildDescriptor(input), CancellationToken.None);
        return Ok(ApiResult.Ok("创建成功"));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] ApplicationInput input)
    {
        if (!IsAdmin())
        {
            return Unauthorized(Errors.NotAuthenticated);
        }
        var app = await applicationManager.FindByIdAsync(id);
        if (app == null)
        {
            return Ok(Errors.UserNotExistResult);
        }

        var descriptor = BuildDescriptor(input);
        // 保留非 scope 的已有权限
        foreach (var p in await applicationManager.GetPermissionsAsync(app))
        {
            if (!p.StartsWith("scp:"))
            {
                descriptor.Permissions.Add(p);
            }
        }

        await applicationManager.UpdateAsync(app, descriptor, CancellationToken.None);
        return Ok(ApiResult.Ok("更新成功"));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        if (!IsAdmin())
        {
            return Unauthorized(Errors.NotAuthenticated);
        }
        var app = await applicationManager.FindByIdAsync(id);
        if (app == null)
        {
            return Ok(Errors.UserNotExistResult);
        }
        await applicationManager.DeleteAsync(app, CancellationToken.None);
        return Ok(ApiResult.Ok("删除成功"));
    }

    private static OpenIddictApplicationDescriptor BuildDescriptor(ApplicationInput input)
    {
        var d = new OpenIddictApplicationDescriptor
        {
            ClientId = input.ClientId, ClientSecret = input.ClientSecret,
            ClientType = input.ClientType ?? "confidential",
            ConsentType = input.ConsentType ?? "implicit", DisplayName = input.DisplayName
        };
        if (!string.IsNullOrEmpty(input.ClientUrl))
        {
            d.Settings["client_url"] = input.ClientUrl;
        }
        if (!string.IsNullOrEmpty(input.ClientLogoUrl))
        {
            d.Settings["client_logo_url"] = input.ClientLogoUrl;
        }
        d.Settings["enabled"] = input.Enabled ? "true" : "false";

        foreach (var u in input.RedirectUris ?? [])
        {
            d.RedirectUris.Add(new Uri(u));
        }
        foreach (var u in input.PostLogoutRedirectUris ?? [])
        {
            d.PostLogoutRedirectUris.Add(new Uri(u));
        }
        foreach (var gt in input.GrantTypes ?? [])
        {
            d.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.GrantType + gt);
        }
        foreach (var sc in input.Scopes ?? [])
        {
            d.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + sc);
        }
        if (input.RedirectUris is { Count: > 0 })
        {
            d.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
            d.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
            d.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Code);
        }

        if (input.PostLogoutRedirectUris is { Count: > 0 })
        {
            d.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.EndSession);
        }
        if (input.RequirePkce)
        {
            d.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);
        }
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
    public bool RequirePkce { get; set; }
    public bool Enabled { get; set; } = true;
}