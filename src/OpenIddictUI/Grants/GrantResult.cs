using System.Security.Claims;

namespace OpenIddictUI.Grants;

public class GrantResult
{
    public ClaimsPrincipal? Principal { get; init; }
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }

    public bool IsError => Error != null;

    public static GrantResult Success(ClaimsPrincipal principal) => new() { Principal = principal };
    public static GrantResult Failure(string error, string description) => new() { Error = error, ErrorDescription = description };
}
