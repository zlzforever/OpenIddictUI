using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddictUI.Grants;

namespace OpenIddictUI.Tests.Grants;

public class BaseGrantHandlerTests
{
    private class TestGrantHandler : BaseGrantHandler
    {
        private readonly GrantResult _result;

        public TestGrantHandler(GrantResult result) => _result = result;

        protected override Task<GrantResult> HandleAsync(OpenIddictRequest request,
            HttpContext context, CancellationToken ct)
            => Task.FromResult(_result);
    }

    [Fact]
    public async Task ExecuteAsync_Success_ReturnsSignInResult()
    {
        var handler = new TestGrantHandler(GrantResult.Success(new ClaimsPrincipal()));
        var ctx = new DefaultHttpContext { RequestServices = CreateServices() };
        var request = new OpenIddictRequest(new Dictionary<string, OpenIddictParameter>());

        var result = await handler.ExecuteAsync(request, ctx, CancellationToken.None);

        result.Should().BeOfType<SignInResult>();
    }

    [Fact]
    public async Task ExecuteAsync_Failure_ReturnsForbidResult()
    {
        var handler = new TestGrantHandler(GrantResult.Failure("invalid_grant", "error"));
        var ctx = new DefaultHttpContext { RequestServices = CreateServices() };
        var request = new OpenIddictRequest(new Dictionary<string, OpenIddictParameter>());

        var result = await handler.ExecuteAsync(request, ctx, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    private static IServiceProvider CreateServices()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        return sc.BuildServiceProvider();
    }
}
