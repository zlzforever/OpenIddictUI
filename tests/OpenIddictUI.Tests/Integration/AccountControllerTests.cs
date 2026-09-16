using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenIddictUI.Controllers;

namespace OpenIddictUI.Tests.Integration;

public class AccountControllerTests
{
    private static readonly HttpClient Client;
    private static readonly WebApplicationFactory<Program> Factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder.UseSetting("OpenIddict:Issuer", "http://localhost"));

    static AccountControllerTests()
    {
        Client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    // ==================== Health / Discovery ====================

    [Fact]
    public async Task HealthCheck_Returns200()
    {
        (await Client.GetAsync("/healthz")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenIdDiscovery_Returns200()
    {
        (await Client.GetAsync("/.well-known/openid-configuration")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ==================== Captcha ====================

    [Fact]
    public async Task CaptchaImage_ReturnsJpeg()
    {
        var r = await Client.GetAsync("/api/v1.0/captcha/image");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
        r.Headers.GetValues("Set-Cookie")
            .Should().Contain(cookie => cookie.StartsWith("openidui-captcha-image=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SliderCaptcha_Init_ReturnsJpeg()
    {
        var r = await Client.GetAsync("/api/v1.0/captcha/slider");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
        r.Headers.GetValues("Set-Cookie")
            .Should().Contain(cookie => cookie.StartsWith("openidui-captcha-slider=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SliderCaptcha_Verify_NoId_ReturnsError()
    {
        var r = await PostJsonWithAntiforgeryAsync("/api/v1.0/captcha/slider/verify", new { });
        var api = await r.Content.ReadFromJsonAsync<ApiResult>();
        api!.Success.Should().BeFalse();
    }

    // ==================== Antiforgery ====================

    [Fact]
    public async Task AntiforgeryToken_ReturnsToken()
    {
        var r = await Client.GetAsync("/api/antiforgery/token");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ==================== Session ====================

    [Fact]
    public async Task Session_WithoutAuth_Returns401()
    {
        (await Client.GetAsync("/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ==================== Login ====================

    [Fact]
    public async Task LoginProviders_ReturnsConfiguredSupportedProviders()
    {
        var response = await Client.GetAsync("/account/providers");
        var api = await response.Content.ReadFromJsonAsync<ApiResult>();

        api!.Success.Should().BeTrue();
        ((JsonElement)api.Data!).EnumerateArray().Select(item => item.GetString())
            .Should().Equal("password", "sms", "weixin");
    }

    [Fact]
    public async Task WeixinLogin_IsCaseInsensitiveAndStartsChallenge()
    {
        var returnUrl =
            HttpUtility.UrlEncode("http://localhost/connect/authorize?client_id=sample-app");

        var url = $"/account/external-login?provider=WEIXIN&returnUrl={returnUrl}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "a.com");
        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.Host.Should().Be("open.weixin.qq.com");

        var location = response.Headers.Location.ToString();
        // https://open.weixin.qq.com/connect/qrconnect?appid=wx035a915c40dc3588&scope=snsapi_login,snsapi_userinfo&response_type=code&redirect_uri=http://localhost/openid/signin-weixin&state=CfDJ8AXVtWCBGZRCiy6ANCUvMAoKikW9rCvPTXmRRvmwNaRBiKPMdvt5_UpukTYjK0y0COqxViFMRIbFc2cbJLiHnb2aNvqo1sW4UoqOoniOR24eaoXCde15D6XuTNZMKs3QtyfBd4Z99W1_GipkxXyvmB8pDo9TlUHqPafX8mYAQf7BrPvX9mZnH1blyWA8Oe_Gnnhaxnbz5oevXrzR2cUDf6NiGPvx4xESS91jr9nGDqB8m3oOJ5UucABcnfNmdVKJ1UzK-RH1obYe_UE2RhZK1L4So0A6-S3yT5wxvHVXRXjEcPXR0ATzKU03ZhhGwNqYcbk6K1yU3Fke_Ch-LClW-RTkXI3gCZaqiqYsuul-ZL6cv-0gpT72PRMwTi-Nl8kF6hY1MySiXMG2uJrY5qd7n6Kp3VlnoHpQTDrj6BL9zF6t2wnP2cCDYm9Mce_PdZt3dg
    }

    [Fact]
    public async Task ExternalBindingStatus_WithoutOAuthTicket_ReturnsExpired()
    {
        var response = await Client.GetAsync("/account/external-binding/status");
        var api = await response.Content.ReadFromJsonAsync<ApiResult>();

        api!.Success.Should().BeFalse();
        api.Code.Should().Be(Errors.ExternalBindingExpired);
    }

    [Fact]
    public async Task Login_EmptyBody_Returns400()
    {
        var r = await PostJsonWithAntiforgeryAsync("/account/login", new { });
        var api = await r.Content.ReadFromJsonAsync<ApiResult>();
        api!.Success.Should().BeFalse();
        api.Code.Should().Be(400);
    }

    [Fact]
    public async Task Login_InvalidReturnUrl_Returns400()
    {
        var r = await PostJsonWithAntiforgeryAsync("/account/login",
            new { username = "u", password = "p", returnUrl = "http://evil.com" });
        (await r.Content.ReadFromJsonAsync<ApiResult>())!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Login_EmptyUsername_Returns400()
    {
        var r = await PostJsonWithAntiforgeryAsync("/account/login", new { username = "", password = "p" });
        var api = await r.Content.ReadFromJsonAsync<ApiResult>();
        api!.Success.Should().BeFalse();
        api.Code.Should().Be(400);
    }

    [Fact]
    public async Task Login_EmptyPassword_Returns400()
    {
        var r = await PostJsonWithAntiforgeryAsync("/account/login", new { username = "u", password = "" });
        var api = await r.Content.ReadFromJsonAsync<ApiResult>();
        api!.Success.Should().BeFalse();
        api.Code.Should().Be(400);
    }

    // ==================== LoginBySms ====================

    [Fact]
    public async Task SmsLogin_EmptyBody_Returns400()
    {
        var r = await PostJsonWithAntiforgeryAsync("/account/login-by-sms", new { });
        var api = await r.Content.ReadFromJsonAsync<ApiResult>();
        api!.Success.Should().BeFalse();
        api.Code.Should().Be(400);
    }

    [Fact]
    public async Task SmsLogin_InvalidReturnUrl_Returns400()
    {
        var r = await PostJsonWithAntiforgeryAsync("/account/login-by-sms",
            new { phoneNumber = "138", verifyCode = "123", returnUrl = "http://evil.com" });
        (await r.Content.ReadFromJsonAsync<ApiResult>())!.Success.Should().BeFalse();
    }

    // ==================== SendSmsCode ====================

    [Fact]
    public async Task SendSmsCode_EmptyBody_Returns400()
    {
        var r = await PostJsonWithAntiforgeryAsync("/account/send-sms-code", new { });
        var api = await r.Content.ReadFromJsonAsync<ApiResult>();
        api!.Success.Should().BeFalse();
        api.Code.Should().Be(400);
    }

    [Fact]
    public async Task SendSmsCode_EmptyPhone_Returns400()
    {
        var r = await PostJsonWithAntiforgeryAsync("/account/send-sms-code", new { phoneNumber = "" });
        var api = await r.Content.ReadFromJsonAsync<ApiResult>();
        api!.Success.Should().BeFalse();
        api.Code.Should().Be(400);
    }

    // ==================== Logout ====================

    [Fact]
    public async Task Logout_ReturnsOk()
    {
        var r = await Client.PostAsJsonAsync("/account/logout", new { });
        var api = await r.Content.ReadFromJsonAsync<ApiResult>();
        api!.Success.Should().BeTrue();
    }

    // ==================== ChangePassword ====================

    [Fact]
    public async Task ChangePassword_EmptyBody_Returns400()
    {
        var r = await Client.PostAsJsonAsync("/account/change-password", new { });
        var api = await r.Content.ReadFromJsonAsync<ApiResult>();
        api!.Success.Should().BeFalse();
        api.Code.Should().Be(400);
    }

    [Fact]
    public async Task ChangePassword_PasswordMismatch_Returns400()
    {
        var r = await Client.PostAsJsonAsync("/account/change-password",
            new { userName = "u", oldPassword = "o", newPassword = "n1", confirmNewPassword = "n2" });
        var api = await r.Content.ReadFromJsonAsync<ApiResult>();
        api!.Success.Should().BeFalse();
        api.Code.Should().Be(400);
    }

    // ==================== ResetPasswordBySms ====================

    [Fact]
    public async Task ResetPasswordBySms_EmptyBody_Returns400()
    {
        var r = await Client.PostAsJsonAsync("/account/reset-password-by-sms", new { });
        var api = await r.Content.ReadFromJsonAsync<ApiResult>();
        api!.Success.Should().BeFalse();
        api.Code.Should().Be(400);
    }

    // ==================== Consent ====================

    [Fact]
    public async Task Consent_WithoutAuth_ReturnsRedirect()
    {
        var r = await Client.GetAsync("/api/consent/anyid");
        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    // ==================== OAuth Endpoints ====================

    [Fact]
    public async Task Authorize_MissingClient_ReturnsError()
    {
        var r = await Client.GetAsync("/connect/authorize");
        r.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Token_InvalidGrant_ReturnsError()
    {
        var r = await Client.PostAsJsonAsync("/connect/token", new { grant_type = "nonexistent" });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<HttpResponseMessage> PostJsonWithAntiforgeryAsync(string url, object body)
    {
        var tokenResponse = await Client.GetAsync("/api/antiforgery/token");
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokenData = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = tokenData.GetProperty("token").GetString();
        token.Should().NotBeNullOrWhiteSpace();

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        return await Client.SendAsync(request);
    }
}
