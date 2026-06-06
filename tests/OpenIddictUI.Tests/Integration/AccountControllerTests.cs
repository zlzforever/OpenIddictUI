using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenIddictUI.Controllers;

namespace OpenIddictUI.Tests.Integration;

public class AccountControllerTests
{
    private static readonly HttpClient Client;

    static AccountControllerTests()
    {
        Client = new WebApplicationFactory<Program>().CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    // ==================== Health / Discovery ====================

    [Fact] public async Task HealthCheck_Returns200() { (await Client.GetAsync("/healthz")).StatusCode.Should().Be(HttpStatusCode.OK); }
    [Fact] public async Task OpenIdDiscovery_Returns200() { (await Client.GetAsync("/.well-known/openid-configuration")).StatusCode.Should().Be(HttpStatusCode.OK); }

    // ==================== Captcha ====================

    [Fact] public async Task CaptchaImage_ReturnsJpeg()
    {
        var r = await Client.GetAsync("/api/v1.0/captcha/image");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
    }

    [Fact] public async Task SliderCaptcha_Init_ReturnsJpeg()
    {
        var r = await Client.GetAsync("/api/v1.0/captcha/slider");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
    }

    [Fact] public async Task SliderCaptcha_Verify_NoId_ReturnsError()
    {
        var r = await Client.PostAsJsonAsync("/api/v1.0/captcha/slider/verify", new { });
        var api = await r.Content.ReadFromJsonAsync<ApiResult>();
        api!.Success.Should().BeFalse();
    }

    // ==================== Antiforgery ====================

    [Fact] public async Task AntiforgeryToken_ReturnsToken()
    {
        var r = await Client.GetAsync("/api/antiforgery/token");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ==================== Session ====================

    [Fact] public async Task Session_WithoutAuth_Returns401() { (await Client.GetAsync("/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized); }

    // ==================== Login ====================

    [Fact] public async Task Login_EmptyBody_Returns400() { var r = await Client.PostAsJsonAsync("/account/login", new { }); var api = await r.Content.ReadFromJsonAsync<ApiResult>(); api!.Success.Should().BeFalse(); api.Code.Should().Be(400); }
    [Fact] public async Task Login_InvalidReturnUrl_Returns400() { var r = await Client.PostAsJsonAsync("/account/login", new { username = "u", password = "p", returnUrl = "http://evil.com" }); (await r.Content.ReadFromJsonAsync<ApiResult>())!.Success.Should().BeFalse(); }
    [Fact] public async Task Login_EmptyUsername_Returns400() { var r = await Client.PostAsJsonAsync("/account/login", new { username = "", password = "p" }); var api = await r.Content.ReadFromJsonAsync<ApiResult>(); api!.Success.Should().BeFalse(); api.Code.Should().Be(400); }
    [Fact] public async Task Login_EmptyPassword_Returns400() { var r = await Client.PostAsJsonAsync("/account/login", new { username = "u", password = "" }); var api = await r.Content.ReadFromJsonAsync<ApiResult>(); api!.Success.Should().BeFalse(); api.Code.Should().Be(400); }

    // ==================== LoginBySms ====================

    [Fact] public async Task SmsLogin_EmptyBody_Returns400() { var r = await Client.PostAsJsonAsync("/account/login-by-sms", new { }); var api = await r.Content.ReadFromJsonAsync<ApiResult>(); api!.Success.Should().BeFalse(); api.Code.Should().Be(400); }
    [Fact] public async Task SmsLogin_InvalidReturnUrl_Returns400() { var r = await Client.PostAsJsonAsync("/account/login-by-sms", new { phoneNumber = "138", verifyCode = "123", returnUrl = "http://evil.com" }); (await r.Content.ReadFromJsonAsync<ApiResult>())!.Success.Should().BeFalse(); }

    // ==================== SendSmsCode ====================

    [Fact] public async Task SendSmsCode_EmptyBody_Returns400()
    {
        var r = await Client.PostAsJsonAsync("/account/send-sms-code", new { });
        var api = await r.Content.ReadFromJsonAsync<ApiResult>();
        api!.Success.Should().BeFalse(); api.Code.Should().Be(400);
    }

    [Fact] public async Task SendSmsCode_EmptyPhone_Returns400()
    {
        var r = await Client.PostAsJsonAsync("/account/send-sms-code", new { phoneNumber = "" });
        var api = await r.Content.ReadFromJsonAsync<ApiResult>();
        api!.Success.Should().BeFalse(); api.Code.Should().Be(400);
    }

    // ==================== Logout ====================

    [Fact] public async Task Logout_ReturnsOk()
    {
        var r = await Client.PostAsJsonAsync("/account/logout", new { });
        var api = await r.Content.ReadFromJsonAsync<ApiResult>();
        api!.Success.Should().BeTrue();
    }

    // ==================== ChangePassword ====================

    [Fact] public async Task ChangePassword_EmptyBody_Returns400()
    {
        var r = await Client.PostAsJsonAsync("/account/change-password", new { });
        var api = await r.Content.ReadFromJsonAsync<ApiResult>();
        api!.Success.Should().BeFalse(); api.Code.Should().Be(400);
    }

    [Fact] public async Task ChangePassword_PasswordMismatch_Returns400()
    {
        var r = await Client.PostAsJsonAsync("/account/change-password",
            new { userName = "u", oldPassword = "o", newPassword = "n1", confirmNewPassword = "n2" });
        var api = await r.Content.ReadFromJsonAsync<ApiResult>();
        api!.Success.Should().BeFalse(); api.Code.Should().Be(400);
    }

    // ==================== ResetPasswordBySms ====================

    [Fact] public async Task ResetPasswordBySms_EmptyBody_Returns400()
    {
        var r = await Client.PostAsJsonAsync("/account/reset-password-by-sms", new { });
        var api = await r.Content.ReadFromJsonAsync<ApiResult>();
        api!.Success.Should().BeFalse(); api.Code.Should().Be(400);
    }

    // ==================== Consent ====================

    [Fact] public async Task Consent_WithoutAuth_ReturnsRedirect()
    {
        var r = await Client.GetAsync("/api/consent/anyid");
        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    // ==================== OAuth Endpoints ====================

    [Fact] public async Task Authorize_MissingClient_ReturnsError()
    {
        var r = await Client.GetAsync("/connect/authorize");
        r.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.InternalServerError);
    }

    [Fact] public async Task Token_InvalidGrant_ReturnsError()
    {
        var r = await Client.PostAsJsonAsync("/connect/token", new { grant_type = "nonexistent" });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
