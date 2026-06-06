using System.Security.Claims;
using OpenIddictUI.Grants;

namespace OpenIddictUI.Tests;

public class GrantResultTests
{
    [Fact]
    public void Success_SetsPrincipal()
    {
        var p = new ClaimsPrincipal();
        var r = GrantResult.Success(p);

        r.Principal.Should().Be(p);
        r.IsError.Should().BeFalse();
        r.Error.Should().BeNull();
    }

    [Fact]
    public void Failure_SetsErrorAndDescription()
    {
        var r = GrantResult.Failure("invalid_grant", "错误描述");

        r.IsError.Should().BeTrue();
        r.Error.Should().Be("invalid_grant");
        r.ErrorDescription.Should().Be("错误描述");
        r.Principal.Should().BeNull();
    }
}
