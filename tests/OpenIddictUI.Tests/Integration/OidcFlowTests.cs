using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenIddictUI.Controllers;
using Xunit.Abstractions;

namespace OpenIddictUI.Tests.Integration;

/// <summary>
/// OAuth 2.0 PKCE 完整流程测试（直连服务器）
/// 环境变量 OPENIDDICT_TEST_URL 指定目标（默认 http://localhost:5164）
/// </summary>
public class OidcFlowTests
{
    private static readonly string BaseUrl;
    private static readonly HttpClient Client;
    private readonly ITestOutputHelper _output;

    static OidcFlowTests()
    {
        BaseUrl = "https://sample.ptkj.cc";
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true };
        Client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
    }

    public OidcFlowTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task FullFlow_AuthorizationCode_PKCE()
    {
        // ===== 0. CSRF token =====
        var response = await Client.GetAsync("/openid/api/antiforgery/token");
        var af = await response.Content.ReadFromJsonAsync<CsrfData>();
        var csrf = af!.Token!;

        // ===== 1. PKCE =====
        var verifier = RandomNumberGenerator.GetHexString(43, true);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Guid.NewGuid().ToString("N");

        // ===== 2. 登录 =====
        var login = await PostJson("/openid/account/login", new
        {
            username = TestUser, password = TestPassword, captchaCode = "",
            button = "login", rememberLogin = false
        }, csrf);
        var loginOk = await login.Content.ReadFromJsonAsync<ApiResult>();
        _output.WriteLine($"登录: {(loginOk!.Success ? "✅" : "❌")} {loginOk.Message}");

        // ===== 3. Authorize → code =====
        var authUrl = $"/openid/connect/authorize?client_id={TestClient}" +
                      $"&redirect_uri={Uri.EscapeDataString(TestRedirect)}" +
                      $"&response_type=code&scope=openid%20profile" +
                      $"&code_challenge={challenge}&code_challenge_method=S256&state={state}";
        var auth = await Client.GetAsync(authUrl);

        for (var i = 0; i < 3; i++)
        {
            var loc = auth.Headers.Location?.ToString() ?? "";
            _output.WriteLine($"Auth redirect: {loc[..Math.Min(80, loc.Length)]}");
            if (loc.StartsWith(TestRedirect)) break;
            if (loc.StartsWith("/openid/consent/"))
            {
                var cid = loc.Split('/').Last();
                var cres = await PostJson($"/openid/api/consent/{cid}",
                    new { button = "yes", scopesConsented = new[] { "openid", "profile" } }, csrf);
                var cd = await cres.Content.ReadFromJsonAsync<ApiResult>();
                auth = await Client.GetAsync(((JsonElement)cd!.Data!).GetProperty("location").ToString());
                continue;
            }

            auth = await Client.GetAsync(loc);
        }

        var final = auth.Headers.Location!.ToString();
        var code = GetQuery(final, "code");
        code.Should().NotBeNullOrEmpty("应成功获取 authorization_code");
        GetQuery(final, "state").Should().Be(state);
        _output.WriteLine($"code: {code![..10]}...");

        // ===== 4. Token =====
        var tokenRes = await Client.PostAsync("/openid/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["client_id"] = TestClient,
                ["code"] = code, ["code_verifier"] = verifier,
                ["redirect_uri"] = TestRedirect
            }));
        tokenRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = await tokenRes.Content.ReadFromJsonAsync<JsonElement>();
        var at = token.GetProperty("access_token").GetString()!;
        var rt = token.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        _output.WriteLine($"access_token: {at[..20]}...");
        _output.WriteLine($"refresh_token: {(rt != null ? rt[..20] + "..." : "(无)")}");

        // ===== 5. UserInfo =====
        var ui = new HttpRequestMessage(HttpMethod.Get, "/openid/connect/userinfo");
        ui.Headers.Authorization = new("Bearer", at);
        var uiRes = await Client.SendAsync(ui);
        uiRes.StatusCode.Should().Be(HttpStatusCode.OK);
        _output.WriteLine($"userinfo: {(await uiRes.Content.ReadAsStringAsync())[..200]}");

        _output.WriteLine("✅ OAuth 完整流程测试通过");
    }

    private async Task<HttpResponseMessage> PostJson(string url, object body, string csrf)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        msg.Headers.Add("X-XSRF-TOKEN", csrf);
        return await Client.SendAsync(msg);
    }

    private static string GetQuery(string url, string key)
    {
        var q = new Uri(url).Query.TrimStart('?');
        return q.Split('&').Select(p => p.Split('='))
            .Where(kv => kv.Length == 2 && kv[0] == key)
            .Select(kv => Uri.UnescapeDataString(kv[1])).FirstOrDefault() ?? "";
    }

    private static string Base64Url(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private const string TestUser = "admin";
    private const string TestPassword = "1qazZAQ!";
    private const string TestClient = "spa-client";
    private const string TestRedirect = "http://localhost:5175/signin-redirect-callback";

    private class CsrfData
    {
        public string Token { get; set; } = string.Empty;
    }
}