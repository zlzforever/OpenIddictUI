using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;

namespace OpenIddictUI.Controllers;

[Authorize]
[Route("api/scopes")]
public class ScopesController(
    IOpenIddictScopeManager scopeManager,
    IOpenIddictApplicationManager appManager) : Controller
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (!IsAdmin())
        {
            return Unauthorized(Errors.NotAuthenticated);
        }
        var scopes = new List<object>();
        await foreach (var scope in scopeManager.ListAsync())
        {
            scopes.Add(new
            {
                id = await scopeManager.GetIdAsync(scope),
                name = await scopeManager.GetNameAsync(scope),
                displayName = await scopeManager.GetDisplayNameAsync(scope),
                description = await scopeManager.GetDescriptionAsync(scope),
                resources = (await scopeManager.GetResourcesAsync(scope)).ToList()
            });
        }

        return Ok(new ApiResult { Data = scopes });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ScopeInput input)
    {
        if (!IsAdmin()) return Unauthorized(Errors.NotAuthenticated);
        if (await scopeManager.FindByNameAsync(input.Name) != null)
            return Ok(Errors.InvalidRequest);

        await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
        {
            Name = input.Name, DisplayName = input.DisplayName, Description = input.Description,
            Resources = { input.Name }
        }, CancellationToken.None);
        return Ok(ApiResult.Ok("创建成功"));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] ScopeInput input)
    {
        if (!IsAdmin())
        {
            return Unauthorized(Errors.NotAuthenticated);
        }

        var scope = await scopeManager.FindByIdAsync(id);
        if (scope == null)
        {
            return Ok(Errors.UserNotExistResult);
        }

        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = input.Name, DisplayName = input.DisplayName, Description = input.Description,
            Resources = { input.Name }
        };
        foreach (var r in await scopeManager.GetResourcesAsync(scope))
        {
            descriptor.Resources.Add(r);
        }

        await scopeManager.UpdateAsync(scope, descriptor, CancellationToken.None);
        return Ok(ApiResult.Ok("更新成功"));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        if (!IsAdmin()) return Unauthorized(Errors.NotAuthenticated);
        var scope = await scopeManager.FindByIdAsync(id);
        if (scope == null) return Ok(Errors.UserNotExistResult);

        var scopeName = await scopeManager.GetNameAsync(scope);
        var referenced = false;
        await foreach (var app in appManager.ListAsync())
        {
            var perms = await appManager.GetPermissionsAsync(app);
            if (perms.Any(p => p == $"scp:{scopeName}"))
            {
                referenced = true;
                break;
            }
        }

        if (referenced)
        {
            return Ok(ApiResult.Error(Errors.ConsentInvalidSelection, $"Scope '{scopeName}' 被 application 引用，无法删除"));
        }

        await scopeManager.DeleteAsync(scope, CancellationToken.None);
        return Ok(ApiResult.Ok("删除成功"));
    }

    private bool IsAdmin() => User.Identity?.Name == "admin";
}

public class ScopeInput
{
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [StringLength(200)] public string? DisplayName { get; set; }
    [StringLength(500)] public string? Description { get; set; }
}