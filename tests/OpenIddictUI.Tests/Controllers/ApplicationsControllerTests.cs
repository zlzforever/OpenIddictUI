using System.Collections.Immutable;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Moq;
using OpenIddict.Abstractions;
using OpenIddictUI.Controllers;
using OpenIddictUI.Grants;

namespace OpenIddictUI.Tests.Controllers;

public class ApplicationsControllerTests
{
    [Fact]
    public async Task Get_ReturnsEditableFieldsWithoutSensitiveCredentials()
    {
        var application = new object();
        var manager = new Mock<IOpenIddictApplicationManager>();
        manager.Setup(x => x.FindByIdAsync("app-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
        manager.Setup(x => x.GetIdAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("app-1");
        manager.Setup(x => x.GetClientIdAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("client-1");
        manager.Setup(x => x.GetDisplayNameAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("Demo");
        manager.Setup(x => x.GetApplicationTypeAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("native");
        manager.Setup(x => x.GetClientTypeAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("confidential");
        manager.Setup(x => x.GetConsentTypeAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("explicit");
        manager.Setup(x => x.GetRedirectUrisAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray.Create("https://client.example/callback"));
        manager.Setup(x => x.GetPostLogoutRedirectUrisAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray.Create("https://client.example/logout"));
        manager.Setup(x => x.GetPermissionsAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray.Create("gt:authorization_code", "scp:openid", "scp:api"));
        manager.Setup(x => x.GetRequirementsAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray.Create(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange));
        manager.Setup(x => x.GetSettingsAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                ["client_url"] = "https://client.example",
                ["client_logo_url"] = "https://client.example/logo.png",
                ["enabled"] = "false",
                [OpenIddictConstants.Settings.TokenLifetimes.AccessToken] = "01:00:00",
                [OpenIddictConstants.Settings.TokenLifetimes.AuthorizationCode] = "00:05:00",
                [OpenIddictConstants.Settings.TokenLifetimes.RefreshToken] = "14.00:00:00",
                [OpenIddictConstants.Settings.TokenLifetimes.IdentityToken] = "01:00:00",
                [OpenIddictConstants.Settings.TokenLifetimes.DeviceCode] = "00:05:00",
                [OpenIddictConstants.Settings.TokenLifetimes.UserCode] = "00:05:00"
            }.ToImmutableDictionary());

        var controller = CreateController(manager);
        var getMethod = typeof(ApplicationsController).GetMethod("Get", [typeof(string)]);
        getMethod.Should().NotBeNull();
        var result = await ((Task<IActionResult>)getMethod!.Invoke(controller, ["app-1"])!);

        var api = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResult>().Subject;
        api.Success.Should().BeTrue();
        api.Data.Should().NotBeNull();
        var data = api.Data!;
        data.GetType().GetProperty("clientSecret").Should().BeNull();
        data.GetType().GetProperty("jsonWebKeySet").Should().BeNull();
        data.GetType().GetProperty("clientId")!.GetValue(data).Should().Be("client-1");
        data.GetType().GetProperty("applicationType")!.GetValue(data).Should().Be("native");
        data.GetType().GetProperty("grantTypes")!.GetValue(data).Should()
            .BeEquivalentTo(new[] { "authorization_code" });
        data.GetType().GetProperty("scopes")!.GetValue(data).Should()
            .BeEquivalentTo(new[] { "openid", "api" });
        data.GetType().GetProperty("requirePkce")!.GetValue(data).Should().Be(true);
        data.GetType().GetProperty("accessTokenLifetime")!.GetValue(data).Should().Be(3600);
        data.GetType().GetProperty("refreshTokenLifetime")!.GetValue(data).Should().Be(1209600);
        data.GetType().GetProperty("enabled")!.GetValue(data).Should().Be("false");
        manager.Verify(x => x.GetJsonWebKeySetAsync(application, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Get_NonAdminReturnsUnauthorizedWithoutLoadingApplication()
    {
        var manager = new Mock<IOpenIddictApplicationManager>();

        var result = await CreateController(manager, "operator").Get("app-1");

        var api = result.Should().BeOfType<UnauthorizedObjectResult>().Subject.Value.Should()
            .BeOfType<ApiResult>().Subject;
        api.Success.Should().BeFalse();
        api.Code.Should().Be(Errors.NotAuthenticated.Code);
        manager.Verify(x => x.FindByIdAsync("app-1", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Get_UnknownIdReturnsUserNotExistResult()
    {
        var manager = new Mock<IOpenIddictApplicationManager>();
        manager.Setup(x => x.FindByIdAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        var result = await CreateController(manager).Get("missing");

        var api = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should()
            .BeOfType<ApiResult>().Subject;
        api.Success.Should().BeFalse();
        api.Code.Should().Be(Errors.UserNotExist);
        api.Message.Should().Be("用户不存在");
        manager.Verify(x => x.GetPermissionsAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_AuthorizationCodeWithoutRedirectUriReturnsValidationError()
    {
        var manager = new Mock<IOpenIddictApplicationManager>();
        var input = new ApplicationInput
        {
            ClientId = "client-1",
            ClientType = "public",
            GrantTypes = ["authorization_code"],
            RedirectUris = []
        };

        var result = await CreateController(manager).Create(input);

        var api = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should()
            .BeOfType<ApiResult>().Subject;
        api.Success.Should().BeFalse();
        api.Code.Should().Be(Errors.InvalidRequest.Code);
        api.Message.Should().Be("authorization_code grant 必须设置 RedirectUris");
        manager.Verify(x => x.FindByClientIdAsync("client-1", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_WithNullCredentials_PreservesExistingCredentials()
    {
        var application = new object();
        var existingKeys = new JsonWebKeySet("{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"old\"}]}");
        var (manager, capture) = CreateUpdateManager(application, existingKeys, "old-secret");

        var result = await CreateController(manager).Update("app-1", ValidUpdateInput());

        AssertSuccess(result);
        capture.Descriptor.Should().NotBeNull();
        capture.Descriptor!.ClientSecret.Should().Be("old-secret");
        capture.Descriptor.JsonWebKeySet!.Keys.Single().Kid.Should().Be("old");
    }

    [Fact]
    public async Task Update_PublicToConfidentialWithoutCredentials_ReturnsValidationError()
    {
        var application = new object();
        var (manager, capture) = CreateUpdateManager(application,
            new JsonWebKeySet("{\"keys\":[{\"kty\":\"EC\",\"kid\":\"public\"}]}"),
            existingSecret: string.Empty,
            existingClientType: "public");

        var result = await CreateController(manager).Update("app-1", ValidUpdateInput());

        var api = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResult>().Subject;
        api.Success.Should().BeFalse();
        api.Code.Should().Be(Errors.InvalidRequest.Code);
        capture.Descriptor.Should().BeNull();
        manager.Verify(x => x.UpdateAsync(
            application, It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_NullClientTypeToConfidentialWithoutCredentials_ReturnsValidationError()
    {
        var application = new object();
        var (manager, capture) = CreateUpdateManager(application,
            new JsonWebKeySet("{\"keys\":[{\"kty\":\"EC\",\"kid\":\"public\"}]}"),
            existingSecret: string.Empty,
            existingClientType: null);

        var result = await CreateController(manager).Update("app-1", ValidUpdateInput());

        var api = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResult>().Subject;
        api.Success.Should().BeFalse();
        api.Code.Should().Be(Errors.InvalidRequest.Code);
        capture.Descriptor.Should().BeNull();
        manager.Verify(x => x.UpdateAsync(
            application, It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_WithNewSecret_PreservesExistingJwks()
    {
        var application = new object();
        var existingKeys = new JsonWebKeySet("{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"old\"}]}");
        var (manager, capture) = CreateUpdateManager(application, existingKeys, "old-secret");
        var input = ValidUpdateInput();
        input.ClientSecret = "new-secret";

        var result = await CreateController(manager).Update("app-1", input);

        AssertSuccess(result);
        capture.Descriptor.Should().NotBeNull();
        capture.Descriptor!.ClientSecret.Should().Be("new-secret");
        capture.Descriptor.JsonWebKeySet!.Keys.Single().Kid.Should().Be("old");
    }

    [Fact]
    public async Task Update_WithNewJwks_PreservesExistingSecret()
    {
        var application = new object();
        var existingKeys = new JsonWebKeySet("{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"old\"}]}");
        var (manager, capture) = CreateUpdateManager(application, existingKeys, "old-secret");
        var input = ValidUpdateInput();
        input.JsonWebKeySet = "{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"new\"}]}";

        var result = await CreateController(manager).Update("app-1", input);

        AssertSuccess(result);
        capture.Descriptor.Should().NotBeNull();
        capture.Descriptor!.ClientSecret.Should().Be("old-secret");
        capture.Descriptor.JsonWebKeySet!.Keys.Single().Kid.Should().Be("new");
    }

    [Fact]
    public async Task Update_PreservesUrisPermissionsGrantsScopesPkceAndTokenLifetimes()
    {
        var application = new object();
        var (manager, capture) = CreateUpdateManager(
            application,
            new JsonWebKeySet("{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"old\"}]}"),
            "old-secret",
            configureExisting: descriptor =>
            {
                descriptor.Permissions.Add("aud:existing-api");
                descriptor.Permissions.Add("gt:old");
                descriptor.Permissions.Add("scp:old");
                descriptor.Requirements.Add("req:old");
                descriptor.RedirectUris.Add(new Uri("https://old.example/callback"));
                descriptor.PostLogoutRedirectUris.Add(new Uri("https://old.example/logout"));
            });
        var input = ValidUpdateInput();
        input.RedirectUris = ["https://new.example/callback"];
        input.PostLogoutRedirectUris = ["https://new.example/logout"];
        input.GrantTypes = ["authorization_code", "refresh_token"];
        input.Scopes = ["openid", "api"];
        input.RequirePkce = true;
        input.AccessTokenLifetime = 61;
        input.AuthorizationCodeLifetime = 302;
        input.RefreshTokenLifetime = 3603;
        input.IdentityTokenLifetime = 3604;
        input.DeviceCodeLifetime = 365;
        input.UserCodeLifetime = 366;

        var result = await CreateController(manager).Update("app-1", input);

        AssertSuccess(result);
        capture.Descriptor.Should().NotBeNull();
        var descriptor = capture.Descriptor!;
        descriptor.RedirectUris.Should().BeEquivalentTo(new[] { new Uri("https://new.example/callback") });
        descriptor.PostLogoutRedirectUris.Should().BeEquivalentTo(new[] { new Uri("https://new.example/logout") });
        descriptor.Permissions.Should().BeEquivalentTo(new[]
        {
            "aud:existing-api",
            "gt:authorization_code",
            "gt:refresh_token",
            OpenIddictConstants.Permissions.ResponseTypes.Code,
            "scp:openid",
            "scp:api",
            OpenIddictConstants.Permissions.Endpoints.Authorization,
            OpenIddictConstants.Permissions.Endpoints.Token,
            OpenIddictConstants.Permissions.Endpoints.EndSession
        });
        descriptor.Requirements.Should().BeEquivalentTo(new[]
        {
            OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange
        });
        descriptor.Settings[OpenIddictConstants.Settings.TokenLifetimes.AccessToken].Should().Be("00:01:01");
        descriptor.Settings[OpenIddictConstants.Settings.TokenLifetimes.AuthorizationCode].Should().Be("00:05:02");
        descriptor.Settings[OpenIddictConstants.Settings.TokenLifetimes.RefreshToken].Should().Be("01:00:03");
        descriptor.Settings[OpenIddictConstants.Settings.TokenLifetimes.IdentityToken].Should().Be("01:00:04");
        descriptor.Settings[OpenIddictConstants.Settings.TokenLifetimes.DeviceCode].Should().Be("00:06:05");
        descriptor.Settings[OpenIddictConstants.Settings.TokenLifetimes.UserCode].Should().Be("00:06:06");
    }

    private static ApplicationsController CreateController(
        Mock<IOpenIddictApplicationManager> manager, string userName = "admin")
    {
        var controller = new ApplicationsController(manager.Object, Array.Empty<IGrantHandler>());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, userName)], "test"))
            }
        };
        return controller;
    }

    private static (Mock<IOpenIddictApplicationManager> Manager, DescriptorCapture Capture) CreateUpdateManager(
        object application,
        JsonWebKeySet existingKeys,
        string existingSecret,
        string? existingClientType = "confidential",
        Action<OpenIddictApplicationDescriptor>? configureExisting = null)
    {
        var capture = new DescriptorCapture();
        var manager = new Mock<IOpenIddictApplicationManager>();
        manager.Setup(x => x.FindByIdAsync("app-1", It.IsAny<CancellationToken>())).ReturnsAsync(application);
        manager.Setup(x => x.GetClientTypeAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingClientType);
        manager.Setup(x => x.GetPermissionsAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);
        manager.Setup(x => x.PopulateAsync(
                It.IsAny<OpenIddictApplicationDescriptor>(), application, It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, object, CancellationToken>((descriptor, _, _) =>
            {
                descriptor.ClientSecret = existingSecret;
                descriptor.JsonWebKeySet = existingKeys;
                configureExisting?.Invoke(descriptor);
            })
            .Returns(ValueTask.CompletedTask);
        manager.Setup(x => x.UpdateAsync(
                application, It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<object, OpenIddictApplicationDescriptor, CancellationToken>((_, descriptor, _) => capture.Descriptor = descriptor)
            .Returns(ValueTask.CompletedTask);
        return (manager, capture);
    }

    private static ApplicationInput ValidUpdateInput() => new()
    {
        ClientId = "client-1",
        ClientType = "confidential",
        ApplicationType = "web",
        ConsentType = "implicit"
    };

    private static void AssertSuccess(IActionResult result)
    {
        var api = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should()
            .BeOfType<ApiResult>().Subject;
        api.Success.Should().BeTrue();
        api.Code.Should().Be(200);
    }

    private sealed class DescriptorCapture
    {
        public OpenIddictApplicationDescriptor? Descriptor { get; set; }
    }
}
