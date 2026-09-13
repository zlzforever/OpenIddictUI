using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using OpenIddictUI.Controllers;
using OpenIddictUI.Grants;

namespace OpenIddictUI.Tests.Integration;

public sealed class ApplicationsControllerHttpTests : IAsyncLifetime
{
    private const string TestScheme = "ApplicationsTest";
    private WebApplication _application = null!;
    private HttpClient _client = null!;
    private Mock<IOpenIddictApplicationManager> _manager = null!;

    public async Task InitializeAsync()
    {
        _manager = new Mock<IOpenIddictApplicationManager>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(_manager.Object);
        builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestScheme;
                options.DefaultChallengeScheme = TestScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, AdminAuthenticationHandler>(TestScheme, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(ApplicationsController).Assembly);

        _application = builder.Build();
        _application.UseRouting();
        _application.UseAuthentication();
        _application.UseAuthorization();
        _application.MapControllers();
        await _application.StartAsync();
        _client = _application.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _application.DisposeAsync();
    }

    [Theory]
    [InlineData("POST", "/api/applications")]
    [InlineData("PUT", "/api/applications/app-1")]
    public async Task InvalidLifetimeJsonReturnsApiResultWithoutWriting(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = new StringContent(
                "{\"clientId\":\"client-1\",\"clientType\":\"public\",\"accessTokenLifetime\":\"oops\"}",
                Encoding.UTF8,
                "application/json")
        };

        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResult>();
        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.Code.Should().Be(400);
        result.Message.Should().NotBeNullOrWhiteSpace();
        _manager.Verify(x => x.FindByClientIdAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _manager.Verify(x => x.FindByIdAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _manager.Verify(x => x.CreateAsync(
            It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()), Times.Never);
        _manager.Verify(x => x.UpdateAsync(
            It.IsAny<object>(), It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("POST", "/api/applications")]
    [InlineData("PUT", "/api/applications/app-1")]
    public async Task NullJsonBodyReturnsApiResultWithoutWriting(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        };

        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResult>();
        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.Code.Should().Be(400);
        result.Message.Should().NotBeNullOrWhiteSpace();
        _manager.Verify(x => x.FindByClientIdAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _manager.Verify(x => x.FindByIdAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("POST", "/api/applications")]
    [InlineData("PUT", "/api/applications/app-1")]
    public async Task EmptyHttpBodyReturnsApiResultWithoutWriting(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
        };

        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResult>();
        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.Code.Should().Be(400);
        result.Message.Should().NotBeNullOrWhiteSpace();
        _manager.Verify(x => x.FindByClientIdAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _manager.Verify(x => x.FindByIdAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _manager.Verify(x => x.CreateAsync(
            It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()), Times.Never);
        _manager.Verify(x => x.UpdateAsync(
            It.IsAny<object>(), It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class AdminAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "admin")], Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
