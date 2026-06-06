using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;

namespace OpenIddictUI.Grants;

public interface IGrantHandler
{
    Task<IActionResult> ExecuteAsync(OpenIddictRequest request, HttpContext context,
        CancellationToken cancellationToken = default);
}