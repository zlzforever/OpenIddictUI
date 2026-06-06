using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OpenIddict.Abstractions;
using OpenIddictUI.Grants;
using OpenIddictUI.Identity;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace OpenIddictUI.Tests.Grants;

public class PhoneCodeGrantHandlerTests
{
    private readonly PhoneCodeGrantHandler _handler = new();

    private static OpenIddictRequest CreateRequest(string phone = "13800138000",
        string code = "123456")
        => new(new Dictionary<string, OpenIddictParameter>
        {
            [Parameters.GrantType] = GrantTypes.Password,
            ["phone_number"] = phone,
            ["code"] = code,
        });

    private static DefaultHttpContext CreateContext()
    {
        var um = new Mock<UserManager<User>>(new Mock<IUserStore<User>>().Object,
            null!, null!, null!, null!, null!, null!, null!, null!).Object;
        var sm = new Mock<SignInManager<User>>(um,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<User>>(),
            null!, null!, null!, null!).Object;

        var services = new ServiceCollection();
        services.AddSingleton(um);
        services.AddSingleton(sm);
        services.AddSingleton(Mock.Of<IOpenIddictScopeManager>());
        services.AddSingleton(Mock.Of<ILogger<PhoneCodeGrantHandler>>());
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    [Fact]
    public async Task EmptyPhoneNumber_ReturnsForbid()
    {
        var r = await _handler.ExecuteAsync(CreateRequest(phone: ""), CreateContext(), CancellationToken.None);
        r.Should().BeOfType<Microsoft.AspNetCore.Mvc.ForbidResult>();
    }

    [Fact]
    public async Task EmptyCode_ReturnsForbid()
    {
        var r = await _handler.ExecuteAsync(CreateRequest(code: ""), CreateContext(), CancellationToken.None);
        r.Should().BeOfType<Microsoft.AspNetCore.Mvc.ForbidResult>();
    }

    [Fact]
    public async Task BothEmpty_ReturnsForbid()
    {
        var r = await _handler.ExecuteAsync(CreateRequest(phone: "", code: ""), CreateContext(), CancellationToken.None);
        r.Should().BeOfType<Microsoft.AspNetCore.Mvc.ForbidResult>();
    }
}
