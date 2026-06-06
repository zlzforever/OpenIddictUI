using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;
using OpenIddictUI.Grants;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace OpenIddictUI.Tests.Grants;

public class BaseGrantHandlerLogicTests
{
    private class TestableHandler : BaseGrantHandler
    {
        protected override Task<GrantResult> HandleAsync(OpenIddictRequest request, HttpContext context,
            CancellationToken ct) => throw new NotImplementedException();

        public new static GrantResult Success(ClaimsPrincipal p) => BaseGrantHandler.Success(p);
        public new static GrantResult Failure(string desc) => BaseGrantHandler.Failure(desc);
        public new static IList<string> GetDestinations(Claim c) => BaseGrantHandler.GetDestinations(c);
    }

    [Fact]
    public void Success_WrapsPrincipal()
    {
        var p = new ClaimsPrincipal();
        var r = TestableHandler.Success(p);
        r.Principal.Should().Be(p);
        r.IsError.Should().BeFalse();
    }

    [Theory]
    [InlineData("用户名错误")]
    [InlineData("验证码不正确")]
    [InlineData("")]
    public void Failure_SetsErrorGrant(string desc)
    {
        var r = TestableHandler.Failure(desc);
        r.IsError.Should().BeTrue();
        r.Error.Should().Be("invalid_grant");
        r.ErrorDescription.Should().Be(desc);
    }

    [Fact]
    public void GetDestinations_NameClaim_ReturnsBoth()
    {
        var c = new Claim(Claims.Name, "test");
        var d = TestableHandler.GetDestinations(c);
        d.Should().Contain(Destinations.AccessToken);
        d.Should().Contain(Destinations.IdentityToken);
    }

    [Fact]
    public void GetDestinations_EmailClaim_ReturnsBoth()
    {
        var c = new Claim(Claims.Email, "test@test.com");
        var d = TestableHandler.GetDestinations(c);
        d.Should().Contain(Destinations.AccessToken);
        d.Should().Contain(Destinations.IdentityToken);
    }

    [Fact]
    public void GetDestinations_PhoneClaim_ReturnsBoth()
    {
        var c = new Claim(Claims.PhoneNumber, "13800138000");
        var d = TestableHandler.GetDestinations(c);
        d.Should().Contain(Destinations.AccessToken);
        d.Should().Contain(Destinations.IdentityToken);
    }

    [Fact]
    public void GetDestinations_RoleClaim_ReturnsBoth()
    {
        var c = new Claim(Claims.Role, "admin");
        var d = TestableHandler.GetDestinations(c);
        d.Should().Contain(Destinations.AccessToken);
        d.Should().Contain(Destinations.IdentityToken);
    }

    [Fact]
    public void GetDestinations_PreferredUsername_ReturnsBoth()
    {
        var c = new Claim(Claims.PreferredUsername, "user1");
        var d = TestableHandler.GetDestinations(c);
        d.Should().Contain(Destinations.AccessToken);
        d.Should().Contain(Destinations.IdentityToken);
    }

    [Fact]
    public void GetDestinations_CustomClaim_ReturnsAccessTokenOnly()
    {
        var c = new Claim("custom_claim", "value");
        var d = TestableHandler.GetDestinations(c);
        d.Should().Contain(Destinations.AccessToken);
        d.Should().NotContain(Destinations.IdentityToken);
    }

    [Fact]
    public void GetDestinations_SubjectClaim_ReturnsAccessTokenOnly()
    {
        var c = new Claim(Claims.Subject, "sub-123");
        var d = TestableHandler.GetDestinations(c);
        d.Should().NotContain(Destinations.IdentityToken);
    }
}
