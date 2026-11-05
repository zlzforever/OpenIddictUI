using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using OpenIddictUI.Api.Controllers;

namespace OpenIddictUI.Tests.Integration;

[CollectionDefinition("Production pipeline environment", DisableParallelization = true)]
public class ProductionPipelineEnvironmentCollection;

[Collection("Production pipeline environment")]
public class TemporaryTokenProductionPipelineTests
{
    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://localhost/openid")]
    public async Task ProductionPipelineAuthenticatesAndIssuesOnlyFromTheValidatedBearer(string authority)
    {
        await using var app = Program.CreateWebApplication([
            "--urls=http://127.0.0.1:0",
            "--applicationName=OpenIddictUI",
            "--Database=postgres",
            "--ConnectionStrings:DefaultConnection=Host=127.0.0.1;Database=isolated;Username=isolated;Password=isolated",
            "--PostgresCache:CreateIfNotExists=false",
            $"--OpenIddict:Issuer={authority}",
            "--TemporaryTokens:Scopes:api1:0=api1",
            "--TemporaryTokens:Audiences:api1:Enabled=true",
            "--TemporaryTokens:Audiences:api1:IndependentClaims:0=role",
            "--TemporaryTokens:Audiences:api1:IndependentClaims:1=business"
        ]);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var credentials = app.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue.SigningCredentials[0];
        var handler = new JsonWebTokenHandler();
        string Token(string type = "at+jwt", string? issuer = null, SecurityKey? key = null, int seconds = 120) =>
            handler.CreateToken(new SecurityTokenDescriptor
            {
                Issuer = issuer ?? new Uri(authority).AbsoluteUri,
                Audience = "api1",
                TokenType = type,
                IssuedAt = DateTime.UtcNow.AddSeconds(-5),
                NotBefore = DateTime.UtcNow.AddSeconds(-5),
                Expires = DateTime.UtcNow.AddSeconds(seconds),
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = "subject",
                    ["scope"] = "api1",
                    ["role"] = new[] { "reader", "operator" },
                    ["business"] = new Dictionary<string, object> { ["count"] = 7, ["enabled"] = true }
                },
                SigningCredentials = key is null ? credentials : new SigningCredentials(key, SecurityAlgorithms.RsaSha256)
            });
        async Task<HttpResponseMessage> Exchange(string? bearer, string path = "/api/temporary-tokens")
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            if (bearer is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            }
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            return await client.SendAsync(request);
        }
        var source = Token();
        using var result = await Exchange(source);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("no-store", result.Headers.CacheControl!.ToString());
        Assert.Contains("no-cache", result.Headers.Pragma.ToString());
        using var body = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
        var output = body.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
        using var sourcePayload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(source.Split('.')[1]));
        using var outputPayload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(output.Split('.')[1]));
        Assert.True(JsonElement.DeepEquals(sourcePayload.RootElement.GetProperty("role"), outputPayload.RootElement.GetProperty("role")));
        Assert.True(JsonElement.DeepEquals(sourcePayload.RootElement.GetProperty("business"), outputPayload.RootElement.GetProperty("business")));
        Assert.Equal("temporary_readonly", outputPayload.RootElement.GetProperty("token_use").GetString());
        Assert.True(outputPayload.RootElement.GetProperty("exp").GetInt64() <= sourcePayload.RootElement.GetProperty("exp").GetInt64());
        Assert.False(body.RootElement.GetProperty("data").TryGetProperty("refreshToken", out _));
        var jwksText = await client.GetStringAsync("/.well-known/jwks");
        using var jwks = JsonDocument.Parse(jwksText);
        Assert.Contains(jwks.RootElement.GetProperty("keys").EnumerateArray(), item => item.GetProperty("kid").GetString() == handler.ReadJsonWebToken(output).Kid);
        using var discovered = JsonDocument.Parse(await client.GetStringAsync("/.well-known/openid-configuration"));
        Assert.Equal(handler.ReadJsonWebToken(source).Issuer, discovered.RootElement.GetProperty("issuer").GetString());
        Assert.Equal(handler.ReadJsonWebToken(output).Issuer, discovered.RootElement.GetProperty("issuer").GetString());
        var metadataBackchannel = new MetadataBackchannel(client, new Uri(authority));
        await using var apiFactory = new WebApplicationFactory<DemoController>().WithWebHostBuilder(webHost =>
        {
            webHost.UseEnvironment("Development");
            webHost.UseSetting("Authority", authority);
            webHost.ConfigureTestServices(services => services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                options => options.Backchannel = new HttpClient(metadataBackchannel)));
        });
        var previousAuthority = Environment.GetEnvironmentVariable("Authority");
        HttpClient api;
        try
        {
            // Minimal hosting reads app configuration before the test host's
            // ConfigureAppConfiguration callbacks. Override the unprefixed Authority
            // during startup; TP_AUTHORITY is a separate configuration key.
            Environment.SetEnvironmentVariable("Authority", authority);
            api = apiFactory.CreateClient();
        }
        finally
        {
            Environment.SetEnvironmentVariable("Authority", previousAuthority);
        }
        using var apiClient = api;
        Assert.Equal(authority, apiFactory.Services.GetRequiredService<IConfiguration>()["Authority"]);
        var apiOptions = apiFactory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.Equal(authority, apiOptions.Authority);
        Assert.Equal(new Uri(authority).AbsoluteUri, apiOptions.TokenValidationParameters.ValidIssuer);
        async Task<HttpStatusCode> CallApi(string token, HttpMethod method, string path)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await api.SendAsync(request);
            return response.StatusCode;
        }
        Assert.Equal(HttpStatusCode.OK, await CallApi(source, HttpMethod.Get, "/api/me"));
        Assert.Contains("/.well-known/openid-configuration", metadataBackchannel.RequestedPaths);
        Assert.Contains("/.well-known/jwks", metadataBackchannel.RequestedPaths);
        Assert.Equal(HttpStatusCode.OK, await CallApi(output, HttpMethod.Get, "/api/me"));
        Assert.Equal(HttpStatusCode.Unauthorized, await CallApi(Token(issuer: "http://elsewhere/"), HttpMethod.Get, "/api/me"));
        var differentSlash = handler.ReadJsonWebToken(source).Issuer.EndsWith('/')
            ? handler.ReadJsonWebToken(source).Issuer.TrimEnd('/')
            : handler.ReadJsonWebToken(source).Issuer + "/";
        Assert.Equal(HttpStatusCode.Unauthorized, await CallApi(Token(issuer: differentSlash), HttpMethod.Get, "/api/me"));
        Assert.Equal(HttpStatusCode.OK, await CallApi(output, HttpMethod.Get, "/api/me"));
        Assert.Equal(HttpStatusCode.Forbidden, await CallApi(output, HttpMethod.Post, "/api/test-write"));
        Assert.Equal(HttpStatusCode.Forbidden, await CallApi(output, HttpMethod.Get, "/api/unclassified"));
        Assert.Equal(HttpStatusCode.OK, await CallApi(source, HttpMethod.Post, "/api/test-write"));
        using var otherKey = RSA.Create(2048);
        var forged = Token(key: new RsaSecurityKey(otherKey) { KeyId = handler.ReadJsonWebToken(source).Kid });
        Assert.Equal(HttpStatusCode.Unauthorized, await CallApi(forged, HttpMethod.Get, "/api/me"));
        Assert.Equal(HttpStatusCode.Unauthorized, await CallApi(Token(seconds: -1), HttpMethod.Get, "/api/me"));
        using var missing = await Exchange(null);
        using var badSignature = await Exchange(forged);
        using var mixed = await Exchange(forged, "/api/temporary-tokens?access_token=" + Uri.EscapeDataString(source));
        using var expired = await Exchange(Token(seconds: -1));
        using var idToken = await Exchange(Token(type: "JWT"));
        using var wrongIssuer = await Exchange(Token(issuer: "http://elsewhere/"));
        using var chain = await Exchange(output);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, badSignature.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, mixed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, idToken.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongIssuer.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, chain.StatusCode);
        Assert.DoesNotContain(output, await chain.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await app.StopAsync();
    }

    private sealed class MetadataBackchannel(HttpClient server, Uri authority) : HttpMessageHandler
    {
        public List<string> RequestedPaths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Scheme != authority.Scheme || uri.Host != authority.Host || uri.Port != authority.Port ||
                !uri.AbsolutePath.StartsWith(authority.AbsolutePath.TrimEnd('/') + "/.well-known/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unexpected metadata URL.");
            }
            var path = uri.AbsolutePath[authority.AbsolutePath.TrimEnd('/').Length..];
            RequestedPaths.Add(path);
            return await server.GetAsync(path + uri.Query, cancellationToken);
        }
    }
}
