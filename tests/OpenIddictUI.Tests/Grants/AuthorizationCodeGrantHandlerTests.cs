using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OpenIddict.Abstractions;
using OpenIddictUI.Grants;

namespace OpenIddictUI.Tests.Grants;

public class AuthorizationCodeGrantHandlerTests
{
    private readonly AuthorizationCodeGrantHandler _handler = new();

    private static DefaultHttpContext CreateContext(AuthenticateResult authResult)
    {
        var authService = new Mock<IAuthenticationService>();
        authService.Setup(s => s.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(authResult);

        var services = new ServiceCollection();
        services.AddSingleton(authService.Object);
        services.AddSingleton(Mock.Of<ILogger<AuthorizationCodeGrantHandler>>());
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private static OpenIddictRequest CreateRequest()
        => new(new Dictionary<string, OpenIddictParameter>());

    [Fact]
    public async Task AuthenticateFails_NoPrincipal_ReturnsForbid()
    {
        var ctx = CreateContext(AuthenticateResult.NoResult());
        var r = await _handler.ExecuteAsync(CreateRequest(), ctx, CancellationToken.None);
        r.Should().BeOfType<Microsoft.AspNetCore.Mvc.ForbidResult>();
    }

    [Fact]
    public async Task AuthenticateFails_FailResult_ReturnsForbid()
    {
        var ctx = CreateContext(AuthenticateResult.Fail("invalid"));
        var r = await _handler.ExecuteAsync(CreateRequest(), ctx, CancellationToken.None);
        r.Should().BeOfType<Microsoft.AspNetCore.Mvc.ForbidResult>();
    }

    [Fact]
    public async Task AuthenticateSucceeds_ReturnsSignIn()
    {
        var principal = new System.Security.Claims.ClaimsPrincipal();
        var props = new AuthenticationProperties();
        var result = AuthenticateResult.Success(new AuthenticationTicket(principal, props, "test"));
        var ctx = CreateContext(result);

        var r = await _handler.ExecuteAsync(CreateRequest(), ctx, CancellationToken.None);
        r.Should().BeOfType<Microsoft.AspNetCore.Mvc.SignInResult>();
    }
}
