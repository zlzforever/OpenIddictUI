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
    public async Task Get_NullClientTypeMapsToPublic()
    {
        var application = new object();
        var manager = new Mock<IOpenIddictApplicationManager>();
        manager.Setup(x => x.FindByIdAsync("app-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
        manager.Setup(x => x.GetIdAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("app-1");
        manager.Setup(x => x.GetClientIdAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("client-1");
        manager.Setup(x => x.GetDisplayNameAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("Demo");
        manager.Setup(x => x.GetApplicationTypeAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("web");
        manager.Setup(x => x.GetClientTypeAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        manager.Setup(x => x.GetConsentTypeAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("implicit");
        manager.Setup(x => x.GetRedirectUrisAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);
        manager.Setup(x => x.GetPostLogoutRedirectUrisAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);
        manager.Setup(x => x.GetPermissionsAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);
        manager.Setup(x => x.GetRequirementsAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);
        manager.Setup(x => x.GetSettingsAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>().ToImmutableDictionary());

        var result = await CreateController(manager).Get("app-1");

        var api = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResult>().Subject;
        var data = api.Data!;
        data.GetType().GetProperty("clientType")!.GetValue(data).Should().Be("public");
    }

    [Fact]
    public async Task Get_MissingOrInvalidLifetimeSettingsReturnNull()
    {
        var application = new object();
        var manager = new Mock<IOpenIddictApplicationManager>();
        manager.Setup(x => x.FindByIdAsync("app-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
        manager.Setup(x => x.GetIdAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("app-1");
        manager.Setup(x => x.GetClientIdAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("client-1");
        manager.Setup(x => x.GetDisplayNameAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("Demo");
        manager.Setup(x => x.GetApplicationTypeAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("web");
        manager.Setup(x => x.GetClientTypeAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync("confidential");
        manager.Setup(x => x.GetConsentTypeAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("implicit");
        manager.Setup(x => x.GetRedirectUrisAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);
        manager.Setup(x => x.GetPostLogoutRedirectUrisAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);
        manager.Setup(x => x.GetPermissionsAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);
        manager.Setup(x => x.GetRequirementsAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);
        manager.Setup(x => x.GetSettingsAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                [OpenIddictConstants.Settings.TokenLifetimes.AccessToken] = "invalid",
                [OpenIddictConstants.Settings.TokenLifetimes.RefreshToken] = "invalid",
                [OpenIddictConstants.Settings.TokenLifetimes.DeviceCode] = "invalid"
            }.ToImmutableDictionary());

        var result = await CreateController(manager).Get("app-1");

        var api = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResult>().Subject;
        var data = api.Data!;
        data.GetType().GetProperty("accessTokenLifetime")!.GetValue(data).Should().BeNull();
        data.GetType().GetProperty("authorizationCodeLifetime")!.GetValue(data).Should().BeNull();
        data.GetType().GetProperty("refreshTokenLifetime")!.GetValue(data).Should().BeNull();
        data.GetType().GetProperty("identityTokenLifetime")!.GetValue(data).Should().BeNull();
        data.GetType().GetProperty("deviceCodeLifetime")!.GetValue(data).Should().BeNull();
        data.GetType().GetProperty("userCodeLifetime")!.GetValue(data).Should().BeNull();
    }

    [Fact]
    public async Task List_NullClientTypeMapsToPublic()
    {
        var application = new object();
        var manager = new Mock<IOpenIddictApplicationManager>();
        manager.Setup(x => x.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .Returns(SingleApplication(application));
        manager.Setup(x => x.GetIdAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("app-1");
        manager.Setup(x => x.GetClientIdAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("client-1");
        manager.Setup(x => x.GetDisplayNameAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("Demo");
        manager.Setup(x => x.GetApplicationTypeAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        manager.Setup(x => x.GetClientTypeAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        manager.Setup(x => x.GetConsentTypeAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        manager.Setup(x => x.GetRedirectUrisAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);
        manager.Setup(x => x.GetPostLogoutRedirectUrisAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);
        manager.Setup(x => x.GetPermissionsAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);
        manager.Setup(x => x.GetSettingsAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>().ToImmutableDictionary());

        var result = await CreateController(manager).List();

        var api = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResult>().Subject;
        var rows = api.Data.Should().BeOfType<List<object>>().Subject;
        rows.Should().ContainSingle();
        rows[0].GetType().GetProperty("clientType")!.GetValue(rows[0]).Should().Be("public");
    }

    [Fact]
    public async Task LegacyNullClientTypeCanBeLoadedAndSavedAsPublicWithoutCredentials()
    {
        var application = new object();
        var capture = new DescriptorCapture();
        var manager = new Mock<IOpenIddictApplicationManager>();
        manager.Setup(x => x.FindByIdAsync("app-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
        manager.Setup(x => x.GetIdAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("app-1");
        manager.Setup(x => x.GetClientIdAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("client-1");
        manager.Setup(x => x.GetDisplayNameAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("Demo");
        manager.Setup(x => x.GetApplicationTypeAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("web");
        manager.Setup(x => x.GetClientTypeAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        manager.Setup(x => x.GetConsentTypeAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("implicit");
        manager.Setup(x => x.GetRedirectUrisAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);
        manager.Setup(x => x.GetPostLogoutRedirectUrisAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);
        manager.Setup(x => x.GetPermissionsAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);
        manager.Setup(x => x.GetRequirementsAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);
        manager.Setup(x => x.GetSettingsAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>().ToImmutableDictionary());
        manager.Setup(x => x.PopulateAsync(
                It.IsAny<OpenIddictApplicationDescriptor>(), application, It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, object, CancellationToken>((descriptor, _, _) =>
            {
                descriptor.ClientSecret = string.Empty;
            })
            .Returns(ValueTask.CompletedTask);
        manager.Setup(x => x.UpdateAsync(
                application, It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<object, OpenIddictApplicationDescriptor, CancellationToken>((_, descriptor, _) =>
                capture.Descriptor = descriptor)
            .Returns(ValueTask.CompletedTask);

        var getResult = await CreateController(manager).Get("app-1");
        var getApi = getResult.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResult>().Subject;
        var detail = getApi.Data!;
        var clientType = detail.GetType().GetProperty("clientType")!.GetValue(detail).Should().BeOfType<string>().Subject;
        clientType.Should().Be("public");

        var input = ValidUpdateInput();
        input.ClientType = clientType;
        var updateResult = await CreateController(manager).Update("app-1", input);

        AssertSuccess(updateResult);
        capture.Descriptor.Should().NotBeNull();
        capture.Descriptor!.ClientType.Should().Be("public");
        capture.Descriptor.ClientSecret.Should().BeNull();
        capture.Descriptor.Requirements.Should().Contain(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);
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
    public async Task Create_PublicClientWithSecretReturnsValidationError()
    {
        var manager = new Mock<IOpenIddictApplicationManager>();
        var input = new ApplicationInput
        {
            ClientId = "client-1",
            ClientType = "public",
            ClientSecret = "secret"
        };

        var result = await CreateController(manager).Create(input);

        var api = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResult>().Subject;
        api.Success.Should().BeFalse();
        api.Code.Should().Be(Errors.InvalidRequest.Code);
        api.Message.Should().Be("public 客户端不能设置 ClientSecret");
        manager.Verify(x => x.FindByClientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_PublicClientBuildsDescriptorWithPkceAndNoSecret()
    {
        var manager = new Mock<IOpenIddictApplicationManager>();
        OpenIddictApplicationDescriptor? captured = null;
        manager.Setup(x => x.FindByClientIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);
        manager.Setup(x => x.CreateAsync(
                It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, CancellationToken>((descriptor, _) => captured = descriptor)
            .Returns(new ValueTask<object>("app-1"));

        var result = await CreateController(manager).Create(new ApplicationInput
        {
            ClientId = "client-1",
            ClientType = "public",
            RequirePkce = false,
            GrantTypes = ["refresh_token"],
            Scopes = ["openid"]
        });

        AssertSuccess(result);
        captured.Should().NotBeNull();
        captured!.ClientType.Should().Be("public");
        captured.ClientSecret.Should().BeNull();
        captured.Requirements.Should().BeEquivalentTo(new[]
        {
            OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange
        });
        captured.Permissions.Should().Contain("gt:refresh_token");
        captured.Permissions.Should().Contain("scp:openid");
    }

    [Fact]
    public async Task Create_InvalidJwksReturnsValidationErrorInsteadOfThrowing()
    {
        var manager = new Mock<IOpenIddictApplicationManager>();
        var input = new ApplicationInput
        {
            ClientId = "client-1",
            ClientType = "confidential",
            ClientSecret = "secret",
            JsonWebKeySet = "{invalid"
        };

        var result = await CreateController(manager).Create(input);

        var api = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResult>().Subject;
        api.Success.Should().BeFalse();
        api.Code.Should().Be(Errors.InvalidRequest.Code);
        api.Message.Should().Be("JsonWebKeySet 格式不合法");
        manager.Verify(x => x.FindByClientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_InvalidRedirectUriReturnsValidationErrorInsteadOfThrowing()
    {
        var manager = new Mock<IOpenIddictApplicationManager>();
        var result = await CreateController(manager).Create(new ApplicationInput
        {
            ClientId = "client-1",
            ClientType = "public",
            RedirectUris = ["not-an-absolute-uri"]
        });

        var api = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResult>().Subject;
        api.Success.Should().BeFalse();
        api.Code.Should().Be(Errors.InvalidRequest.Code);
        api.Message.Should().Be("RedirectUri 必须是合法的绝对 URI");
        manager.Verify(x => x.FindByClientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_InvalidPostLogoutRedirectUriReturnsValidationErrorInsteadOfThrowing()
    {
        var manager = new Mock<IOpenIddictApplicationManager>();
        var result = await CreateController(manager).Create(new ApplicationInput
        {
            ClientId = "client-1",
            ClientType = "public",
            PostLogoutRedirectUris = ["not-an-absolute-uri"]
        });

        var api = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResult>().Subject;
        api.Success.Should().BeFalse();
        api.Code.Should().Be(Errors.InvalidRequest.Code);
        api.Message.Should().Be("PostLogoutRedirectUri 必须是合法的绝对 URI");
        manager.Verify(x => x.FindByClientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_InvalidRedirectUriReturnsValidationErrorInsteadOfThrowing()
    {
        var application = new object();
        var manager = new Mock<IOpenIddictApplicationManager>();
        manager.Setup(x => x.FindByIdAsync("app-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
        var input = ValidUpdateInput();
        input.ClientType = "public";
        input.RedirectUris = ["not-an-absolute-uri"];

        var result = await CreateController(manager).Update("app-1", input);

        var api = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResult>().Subject;
        api.Success.Should().BeFalse();
        api.Code.Should().Be(Errors.InvalidRequest.Code);
        api.Message.Should().Be("RedirectUri 必须是合法的绝对 URI");
        manager.Verify(x => x.PopulateAsync(
            It.IsAny<OpenIddictApplicationDescriptor>(), application, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_InvalidPostLogoutRedirectUriReturnsValidationErrorInsteadOfThrowing()
    {
        var application = new object();
        var manager = new Mock<IOpenIddictApplicationManager>();
        manager.Setup(x => x.FindByIdAsync("app-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
        var input = ValidUpdateInput();
        input.ClientType = "public";
        input.PostLogoutRedirectUris = ["not-an-absolute-uri"];

        var result = await CreateController(manager).Update("app-1", input);

        var api = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResult>().Subject;
        api.Success.Should().BeFalse();
        api.Code.Should().Be(Errors.InvalidRequest.Code);
        api.Message.Should().Be("PostLogoutRedirectUri 必须是合法的绝对 URI");
        manager.Verify(x => x.PopulateAsync(
            It.IsAny<OpenIddictApplicationDescriptor>(), application, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(nameof(ApplicationInput.AccessTokenLifetime), 0, "AccessTokenLifetime 必须大于 0")]
    [InlineData(nameof(ApplicationInput.AuthorizationCodeLifetime), 0, "AuthorizationCodeLifetime 必须大于 0")]
    [InlineData(nameof(ApplicationInput.RefreshTokenLifetime), 0, "RefreshTokenLifetime 必须大于 0")]
    [InlineData(nameof(ApplicationInput.IdentityTokenLifetime), 0, "IdentityTokenLifetime 必须大于 0")]
    [InlineData(nameof(ApplicationInput.DeviceCodeLifetime), 0, "DeviceCodeLifetime 必须大于 0")]
    [InlineData(nameof(ApplicationInput.UserCodeLifetime), 0, "UserCodeLifetime 必须大于 0")]
    [InlineData(nameof(ApplicationInput.AccessTokenLifetime), -1, "AccessTokenLifetime 必须大于 0")]
    [InlineData(nameof(ApplicationInput.AuthorizationCodeLifetime), -1, "AuthorizationCodeLifetime 必须大于 0")]
    [InlineData(nameof(ApplicationInput.RefreshTokenLifetime), -1, "RefreshTokenLifetime 必须大于 0")]
    [InlineData(nameof(ApplicationInput.IdentityTokenLifetime), -1, "IdentityTokenLifetime 必须大于 0")]
    [InlineData(nameof(ApplicationInput.DeviceCodeLifetime), -1, "DeviceCodeLifetime 必须大于 0")]
    [InlineData(nameof(ApplicationInput.UserCodeLifetime), -1, "UserCodeLifetime 必须大于 0")]
    public async Task Create_NonPositiveLifetimeReturnsValidationError(string propertyName, int value, string message)
    {
        var manager = new Mock<IOpenIddictApplicationManager>();
        var input = new ApplicationInput
        {
            ClientId = "client-1",
            ClientType = "public"
        };
        typeof(ApplicationInput).GetProperty(propertyName)!.SetValue(input, value);

        var result = await CreateController(manager).Create(input);

        var api = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResult>().Subject;
        api.Success.Should().BeFalse();
        api.Code.Should().Be(Errors.InvalidRequest.Code);
        api.Message.Should().Be(message);
        manager.Verify(x => x.FindByClientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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

    [Fact]
    public async Task Update_WhenLifetimesAreOmittedPreservesAllExistingLifetimes()
    {
        var application = new object();
        var (manager, capture) = CreateUpdateManager(
            application,
            new JsonWebKeySet("{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"old\"}]}"),
            "old-secret",
            configureExisting: descriptor =>
            {
                descriptor.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);
                descriptor.SetAccessTokenLifetime(TimeSpan.FromSeconds(61));
                descriptor.SetAuthorizationCodeLifetime(TimeSpan.FromSeconds(302));
                descriptor.SetRefreshTokenLifetime(TimeSpan.FromSeconds(3603));
                descriptor.SetIdentityTokenLifetime(TimeSpan.FromSeconds(3604));
                descriptor.SetDeviceCodeLifetime(TimeSpan.FromSeconds(365));
                descriptor.SetUserCodeLifetime(TimeSpan.FromSeconds(366));
            });

        var result = await CreateController(manager).Update("app-1", ValidUpdateInput());

        AssertSuccess(result);
        capture.Descriptor.Should().NotBeNull();
        var settings = capture.Descriptor!.Settings;
        settings[OpenIddictConstants.Settings.TokenLifetimes.AccessToken].Should().Be("00:01:01");
        settings[OpenIddictConstants.Settings.TokenLifetimes.AuthorizationCode].Should().Be("00:05:02");
        settings[OpenIddictConstants.Settings.TokenLifetimes.RefreshToken].Should().Be("01:00:03");
        settings[OpenIddictConstants.Settings.TokenLifetimes.IdentityToken].Should().Be("01:00:04");
        settings[OpenIddictConstants.Settings.TokenLifetimes.DeviceCode].Should().Be("00:06:05");
        settings[OpenIddictConstants.Settings.TokenLifetimes.UserCode].Should().Be("00:06:06");
        capture.Descriptor.Requirements.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_RemovingAuthorizationCodeDropsDerivedResponsePermission()
    {
        var application = new object();
        var (manager, capture) = CreateUpdateManager(
            application,
            new JsonWebKeySet("{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"old\"}]}"),
            "old-secret",
            configureExisting: descriptor =>
            {
                descriptor.Permissions.Add("aud:existing-api");
                descriptor.Permissions.Add("gt:authorization_code");
                descriptor.Permissions.Add("scp:old");
                descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Code);
                descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
                descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
                descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.EndSession);
            });
        var input = ValidUpdateInput();
        input.RedirectUris = ["https://new.example/callback"];
        input.PostLogoutRedirectUris = [];
        input.GrantTypes = ["refresh_token"];
        input.Scopes = ["api"];

        var result = await CreateController(manager).Update("app-1", input);

        AssertSuccess(result);
        capture.Descriptor.Should().NotBeNull();
        capture.Descriptor!.Permissions.Should().BeEquivalentTo(new[]
        {
            "aud:existing-api",
            "gt:refresh_token",
            "scp:api",
            OpenIddictConstants.Permissions.Endpoints.Authorization,
            OpenIddictConstants.Permissions.Endpoints.Token
        });
    }

    [Fact]
    public async Task Update_WhenUrisAreClearedDropsAllDerivedPermissions()
    {
        var application = new object();
        var (manager, capture) = CreateUpdateManager(
            application,
            new JsonWebKeySet("{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"old\"}]}"),
            "old-secret",
            configureExisting: descriptor =>
            {
                descriptor.Permissions.Add("aud:existing-api");
                descriptor.Permissions.Add("gt:authorization_code");
                descriptor.Permissions.Add("scp:old");
                descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Code);
                descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
                descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
                descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.EndSession);
            });
        var input = ValidUpdateInput();
        input.RedirectUris = [];
        input.PostLogoutRedirectUris = [];
        input.GrantTypes = [];
        input.Scopes = [];

        var result = await CreateController(manager).Update("app-1", input);

        AssertSuccess(result);
        capture.Descriptor.Should().NotBeNull();
        capture.Descriptor!.Permissions.Should().BeEquivalentTo(new[] { "aud:existing-api" });
    }

    [Fact]
    public async Task Update_InvalidJwksReturnsValidationErrorBeforeLoadingApplication()
    {
        var manager = new Mock<IOpenIddictApplicationManager>();
        var input = ValidUpdateInput();
        input.JsonWebKeySet = "{invalid";

        var result = await CreateController(manager).Update("app-1", input);

        var api = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResult>().Subject;
        api.Success.Should().BeFalse();
        api.Code.Should().Be(Errors.InvalidRequest.Code);
        api.Message.Should().Be("JsonWebKeySet 格式不合法");
        manager.Verify(x => x.FindByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_UnknownIdReturnsUserNotExistResult()
    {
        var manager = new Mock<IOpenIddictApplicationManager>();
        manager.Setup(x => x.FindByIdAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        var result = await CreateController(manager).Update("missing", ValidUpdateInput());

        var api = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ApiResult>().Subject;
        api.Success.Should().BeFalse();
        api.Code.Should().Be(Errors.UserNotExist);
        api.Message.Should().Be("用户不存在");
        manager.Verify(x => x.PopulateAsync(
            It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_EmptyDisplayUrlsPreserveExistingSettings()
    {
        var application = new object();
        var (manager, capture) = CreateUpdateManager(
            application,
            new JsonWebKeySet("{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"old\"}]}"),
            "old-secret",
            configureExisting: descriptor =>
            {
                descriptor.Settings["client_url"] = "https://old.example";
                descriptor.Settings["client_logo_url"] = "https://old.example/logo.png";
            });

        var result = await CreateController(manager).Update("app-1", ValidUpdateInput());

        AssertSuccess(result);
        capture.Descriptor.Should().NotBeNull();
        capture.Descriptor!.Settings["client_url"].Should().Be("https://old.example");
        capture.Descriptor.Settings["client_logo_url"].Should().Be("https://old.example/logo.png");
    }

    [Fact]
    public async Task Update_ProvidedDisplayUrlsOverrideExistingSettings()
    {
        var application = new object();
        var (manager, capture) = CreateUpdateManager(
            application,
            new JsonWebKeySet("{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"old\"}]}"),
            "old-secret",
            configureExisting: descriptor =>
            {
                descriptor.Settings["client_url"] = "https://old.example";
                descriptor.Settings["client_logo_url"] = "https://old.example/logo.png";
            });
        var input = ValidUpdateInput();
        input.ClientUrl = "https://new.example";
        input.ClientLogoUrl = "https://new.example/logo.png";

        var result = await CreateController(manager).Update("app-1", input);

        AssertSuccess(result);
        capture.Descriptor.Should().NotBeNull();
        capture.Descriptor!.Settings["client_url"].Should().Be("https://new.example");
        capture.Descriptor.Settings["client_logo_url"].Should().Be("https://new.example/logo.png");
    }

    [Fact]
    public async Task Update_EnabledFalsePersistsDisabledSetting()
    {
        var application = new object();
        var (manager, capture) = CreateUpdateManager(
            application,
            new JsonWebKeySet("{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"old\"}]}"),
            "old-secret",
            configureExisting: descriptor => descriptor.Settings["enabled"] = "true");
        var input = ValidUpdateInput();
        input.Enabled = false;

        var result = await CreateController(manager).Update("app-1", input);

        AssertSuccess(result);
        capture.Descriptor.Should().NotBeNull();
        capture.Descriptor!.Settings["enabled"].Should().Be("false");
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

    private static async IAsyncEnumerable<object> SingleApplication(object application)
    {
        await Task.CompletedTask;
        yield return application;
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
