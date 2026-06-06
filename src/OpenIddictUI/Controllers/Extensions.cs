using System.Security.Claims;
using OpenIddict.Abstractions;

namespace OpenIddictUI.Controllers;

public static class ControllerExtensions
{
    public static string GetFullRequestUri(this HttpContext context)
    {
        var path = context.Request.PathBase + context.Request.Path;
        var query = context.Request.QueryString;
        return path + query;
    }

    public static IEnumerable<string> GetDestinations(this ClaimsPrincipal principal, string claimType)
    {
        var claim = principal.FindFirst(claimType);
        if (claim == null)
        {
            return [];
        }
        return claim.GetDestinations();
    }
}
