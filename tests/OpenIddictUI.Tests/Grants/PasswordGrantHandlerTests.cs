using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OpenIddict.Abstractions;
using OpenIddictUI.Grants;
using OpenIddictUI.Identity;
using OpenIddictUI.Options;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace OpenIddictUI.Tests.Grants;

public class PasswordGrantHandlerTests
{
    private readonly PasswordGrantHandler _handler = new();

    private static OpenIddictRequest CreateRequest(string username = "testuser",
        string password = "P@ss1234", string grantType = "password")
        => new(new Dictionary<string, OpenIddictParameter>
        {
            [Parameters.GrantType] = grantType,
            [Parameters.Username] = username,
            [Parameters.Password] = password,
            [Parameters.ClientId] = "test-client",
            [Parameters.Scope] = "openid profile api1",
        });

    private static (HttpContext Context, Mock<UserManager<User>> Um,
        Mock<SignInManager<User>> Sm) CreateMocks()
    {
        var services = new ServiceCollection();
        var store = Mock.Of<IUserStore<User>>();
        var um = new Mock<UserManager<User>>(store, null!, null!, null!, null!, null!, null!, null!, null!);
        var sm = new Mock<SignInManager<User>>(
            um.Object, Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<User>>(),
            null!, null!, null!, null!);

        services.AddSingleton(Mock.Of<ILogger<PasswordGrantHandler>>());
        services.AddSingleton(um.Object);
        services.AddSingleton(sm.Object);
        var scopeManager = Mock.Of<IOpenIddictScopeManager>();
        Mock.Get(scopeManager)
            .Setup(m => m.ListResourcesAsync(It.IsAny<System.Collections.Immutable.ImmutableArray<string>>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<string>());

        services.AddSingleton(scopeManager);
        services.AddSingleton(Mock.Of<HybridCache>());
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new OpenIddictUIOptions()));

        return (new DefaultHttpContext { RequestServices = services.BuildServiceProvider() }, um, sm);
    }

    [Fact]
    public async Task ExecuteAsync_UserNotFound_ReturnsForbid()
    {
        var (ctx, um, _) = CreateMocks();
        um.Setup(m => m.FindByNameAsync("nobody")).ReturnsAsync((User?)null);

        var r = await _handler.ExecuteAsync(CreateRequest(username: "nobody"), ctx, CancellationToken.None);
        r.Should().BeOfType<Microsoft.AspNetCore.Mvc.ForbidResult>();
    }

    [Fact]
    public async Task ExecuteAsync_UserLockedOut_ReturnsForbid()
    {
        var (ctx, um, _) = CreateMocks();
        var user = new User();
        um.Setup(m => m.FindByNameAsync("locked")).ReturnsAsync(user);
        um.Setup(m => m.IsLockedOutAsync(user)).ReturnsAsync(true);

        var r = await _handler.ExecuteAsync(CreateRequest(username: "locked"), ctx, CancellationToken.None);
        r.Should().BeOfType<Microsoft.AspNetCore.Mvc.ForbidResult>();
    }

    [Fact]
    public async Task ExecuteAsync_InvalidPassword_ReturnsForbid()
    {
        var (ctx, um, sm) = CreateMocks();
        var user = new User { UserName = "testuser" };
        um.Setup(m => m.FindByNameAsync("testuser")).ReturnsAsync(user);
        um.Setup(m => m.IsLockedOutAsync(user)).ReturnsAsync(false);
        sm.Setup(m => m.CheckPasswordSignInAsync(user, "wrong", true))
            .ReturnsAsync(SignInResult.Failed);

        var r = await _handler.ExecuteAsync(CreateRequest(password: "wrong"), ctx, CancellationToken.None);
        r.Should().BeOfType<Microsoft.AspNetCore.Mvc.ForbidResult>();
    }

    [Fact]
    public async Task ExecuteAsync_ValidCredentials_ReturnsSignIn()
    {
        var (ctx, um, sm) = CreateMocks();
        var user = new User { UserName = "testuser" };
        um.Setup(m => m.FindByNameAsync("testuser")).ReturnsAsync(user);
        um.Setup(m => m.IsLockedOutAsync(user)).ReturnsAsync(false);
        sm.Setup(m => m.CheckPasswordSignInAsync(user, "P@ss1234", true))
            .ReturnsAsync(SignInResult.Success);
        sm.Setup(m => m.CreateUserPrincipalAsync(user))
            .ReturnsAsync(new ClaimsPrincipal(new ClaimsIdentity("password")));

        var r = await _handler.ExecuteAsync(CreateRequest(), ctx, CancellationToken.None);
        r.Should().BeOfType<Microsoft.AspNetCore.Mvc.SignInResult>();
    }
}
