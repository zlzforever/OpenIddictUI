using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using OpenIddict.Validation;
using OpenIddictUI.Api.Controllers;
using OpenIddictUI.Controllers;
using OpenIddictUI.TemporaryTokens;

namespace OpenIddictUI.Tests.Integration;

public class TemporaryTokenSecurityGateTests
{
    [Fact]
    public async Task LocalValidation_AuthenticatesSignedAccessTokensAndRejectsInvalidTokens()
    {
        using var rsa = RSA.Create(2048);
        using var otherRsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=isolated-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TemporaryTokens:Scopes:api1:0"] = "api1",
            ["TemporaryTokens:Audiences:api1:Enabled"] = "true",
            ["TemporaryTokens:Audiences:api1:IndependentClaims:0"] = "role",
            ["TemporaryTokens:Audiences:api1:IndependentClaims:1"] = "business",
            ["TemporaryTokens:Audiences:api1:IndependentClaims:2"] = "client_id"
        });
        builder.Services.AddSingleton<TemporaryTokenService>();
        builder.Services.AddControllers().AddApplicationPart(typeof(TemporaryTokensController).Assembly);
        builder.Services.AddAuthorization();
        // Match production registration order as well as values.
        builder.Services.PostConfigure<OpenIddictValidationOptions>(options =>
        {
            options.TokenValidationParameters.ValidateLifetime = true;
            options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
        });
        builder.Services.AddAuthentication();
        builder.Services.AddOpenIddict().AddServer(options =>
        {
            options.SetIssuer(new Uri("http://localhost/"));
            options.SetTokenEndpointUris("/connect/token");
            options.AllowClientCredentialsFlow();
            options.EnableDegradedMode();
            options.AddEventHandler<OpenIddictServerEvents.ValidateTokenRequestContext>(eventBuilder =>
                eventBuilder.UseInlineHandler(context =>
                {
                    if (context.ClientId != "isolated-client")
                    {
                        context.Reject();
                    }
                    return default;
                }));
            options.AddEventHandler<OpenIddictServerEvents.HandleTokenRequestContext>(eventBuilder =>
                eventBuilder.UseInlineHandler(context =>
                {
                    var identity = new ClaimsIdentity("isolated");
                    identity.SetClaim(OpenIddictConstants.Claims.Subject, "server-issued-subject");
                    identity.SetAuthorizationId("isolated-authorization-id");
                    identity.SetScopes("api1");
                    identity.SetResources("api1");
                    context.SignIn(new ClaimsPrincipal(identity));
                    return default;
                }));
            options.DisableAccessTokenEncryption();
            options.AddSigningCertificate(certificate);
            options.AddEphemeralEncryptionKey();
            options.UseAspNetCore().DisableTransportSecurityRequirement();
        }).AddValidation(options =>
        {
            options.UseLocalServer();
            options.UseAspNetCore();
            options.RejectTemporaryTokenProofOfPossession();
        });
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapGet("/check", async (HttpContext context) =>
        {
            var result = await context.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
            return result.Succeeded ? Results.Ok() : Results.Unauthorized();
        });
        app.MapPost("/exchange", async (HttpContext context) =>
        {
            // The only value parsed below is the exact bearer that this request's
            // OpenIddict authentication scheme has just authenticated.
            var authentication = await context.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
            if (!authentication.Succeeded || !AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization, out var header) ||
                !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(header.Parameter))
            {
                return Results.Unauthorized();
            }
            try
            {
                var parts = header.Parameter.Split('.');
                if (parts.Length != 3)
                {
                    return Results.StatusCode(403);
                }
                using var document = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[1]));
                var source = document.RootElement;
                if (!source.TryGetProperty("scope", out var sourceScope) || sourceScope.GetString() != "api1" ||
                    !source.TryGetProperty("aud", out var sourceAudience) || sourceAudience.ValueKind != JsonValueKind.String || sourceAudience.GetString() != "api1")
                {
                    return Results.StatusCode(403);
                }
                // This isolated example assumes a reviewed receiver contract for
                // these two custom claims. Everything else fails closed.
                string[] permitted = ["sub", "jti", "iss", "aud", "scope", "iat", "nbf", "exp", "role", "business", "client_id", "oi_tkn_id", "oi_au_id", "oi_prst"];
                if (source.EnumerateObject().Any(property => !permitted.Contains(property.Name, StringComparer.Ordinal)))
                {
                    return Results.StatusCode(403);
                }
                var payload = BuildTestPayload(document.RootElement);
                var credentials = context.RequestServices.GetRequiredService<IOptions<OpenIddictServerOptions>>().Value.SigningCredentials.Single();
                var output = new JsonWebTokenHandler().CreateToken(payload, credentials, new Dictionary<string, object> { ["typ"] = "at+jwt" });
                return Results.Text(output);
            }
            catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
            {
                return Results.StatusCode(403);
            }
        });
        await app.StartAsync();
        var client = app.GetTestClient();
        var credentials = app.Services.GetRequiredService<IOptions<OpenIddictServerOptions>>().Value.SigningCredentials.Single();
        var handler = new JsonWebTokenHandler();
        string CreateToken(string type = "at+jwt", string issuer = "http://localhost/", SecurityKey? signingKey = null,
            DateTime? expires = null, DateTime? notBefore = null, string audience = "api1", string scope = "api1") => handler.CreateToken(new SecurityTokenDescriptor
            {
                Issuer = issuer,
                Audience = audience,
                TokenType = type,
                IssuedAt = DateTime.UtcNow.AddMinutes(-2),
                NotBefore = notBefore ?? DateTime.UtcNow.AddMinutes(-1),
                Expires = expires ?? DateTime.UtcNow.AddMinutes(5),
                Claims = new Dictionary<string, object> { ["sub"] = "subject", ["jti"] = "source-id", ["scope"] = scope, ["role"] = new[] { "reader", "operator" }, ["business"] = new Dictionary<string, object> { ["flags"] = new[] { true, false }, ["count"] = 3, ["nested"] = new Dictionary<string, object> { ["value"] = "测试" } } },
                SigningCredentials = signingKey is null ? credentials : new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256)
            });
        async Task<HttpStatusCode> Authenticate(string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/check");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }

        var token = CreateToken();
        var jwt = handler.ReadJsonWebToken(token);
        Assert.Equal("at+jwt", jwt.Typ);
        Assert.False(string.IsNullOrEmpty(jwt.Kid));
        Assert.Equal(SecurityAlgorithms.RsaSha256, jwt.Alg);
        Assert.Equal(HttpStatusCode.OK, await Authenticate(token));
        using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(token.Split('.')[1]));
        var business = payload.RootElement.GetProperty("business");
        Assert.Equal(3, business.GetProperty("count").GetInt32());
        Assert.Equal(JsonValueKind.True, business.GetProperty("flags")[0].ValueKind);
        Assert.Equal(JsonValueKind.False, business.GetProperty("flags")[1].ValueKind);
        Assert.Equal(HttpStatusCode.Unauthorized, await Authenticate(CreateToken(type: "JWT")));
        Assert.Equal(HttpStatusCode.Unauthorized, await Authenticate(CreateToken(issuer: "http://other/")));
        Assert.Equal(HttpStatusCode.Unauthorized, await Authenticate(CreateToken(signingKey: new RsaSecurityKey(otherRsa) { KeyId = jwt.Kid })));
        Assert.Equal(HttpStatusCode.Unauthorized, await Authenticate(CreateToken(expires: DateTime.UtcNow.AddMinutes(-10))));
        Assert.Equal(HttpStatusCode.Unauthorized, await Authenticate(CreateToken(expires: DateTime.UtcNow.AddSeconds(-1))));
        Assert.Equal(HttpStatusCode.Unauthorized, await Authenticate(CreateToken(notBefore: DateTime.UtcNow.AddMinutes(10), expires: DateTime.UtcNow.AddMinutes(15))));
        using var discovery = JsonDocument.Parse(await client.GetStringAsync("/.well-known/openid-configuration"));
        var jwksUri = discovery.RootElement.GetProperty("jwks_uri").GetString();
        using var jwks = JsonDocument.Parse(await client.GetStringAsync(jwksUri));
        Assert.Contains(jwks.RootElement.GetProperty("keys").EnumerateArray(), item => item.GetProperty("kid").GetString() == jwt.Kid);
        using var serverResponse = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "isolated-client",
            ["client_secret"] = "isolated-not-a-secret"
        }));
        var serverContent = await serverResponse.Content.ReadAsStringAsync();
        Assert.True(serverResponse.IsSuccessStatusCode, serverContent);
        using var serverJson = JsonDocument.Parse(serverContent);
        var serverToken = serverJson.RootElement.GetProperty("access_token").GetString()!;
        Assert.Equal(HttpStatusCode.OK, await Authenticate(serverToken));
        using var serverPayload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(serverToken.Split('.')[1]));
        Assert.Equal("isolated-authorization-id", serverPayload.RootElement.GetProperty("oi_au_id").GetString());
        using (var rebuilt = JsonDocument.Parse(BuildTestPayload(serverPayload.RootElement)))
        {
            Assert.False(rebuilt.RootElement.TryGetProperty("oi_au_id", out _));
        }

        // Exercise the actual demo API controller and JWT Bearer middleware. Only test host
        // settings are overridden; discovery and JWKS travel over the issuer TestServer.
        await using var factory = new WebApplicationFactory<DemoController>().WithWebHostBuilder(webHost =>
        {
            webHost.UseEnvironment("Development");
            webHost.UseSetting("Authority", "http://localhost/");
            webHost.ConfigureTestServices(services => services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var retriever = new HttpDocumentRetriever(new HttpClient(app.GetTestServer().CreateHandler())) { RequireHttps = false };
                options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    "http://localhost/.well-known/openid-configuration", new OpenIdConnectConfigurationRetriever(), retriever);
            }));
        });
        using var api = factory.CreateClient();
        async Task<HttpStatusCode> CallApi(string bearer)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            using var response = await api.SendAsync(request);
            return response.StatusCode;
        }
        Assert.Equal(HttpStatusCode.OK, await CallApi(token));
        Assert.Equal(HttpStatusCode.OK, await CallApi(serverToken));
        async Task<HttpResponseMessage> CreateTemporary(string? bearer, string input = "{}")
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/api/temporary-tokens");
            if (bearer is not null)
            {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            }
            message.Content = new StringContent(input, Encoding.UTF8, "application/json");
            return await client.SendAsync(message);
        }
        using var created = await CreateTemporary(token);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal("no-store", created.Headers.CacheControl!.ToString());
        Assert.Contains("no-cache", created.Headers.Pragma.ToString());
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var temporary = createdJson.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
        Assert.Equal("Bearer", createdJson.RootElement.GetProperty("data").GetProperty("tokenType").GetString());
        Assert.False(createdJson.RootElement.GetProperty("data").TryGetProperty("refreshToken", out _));
        Assert.DoesNotContain(created.Headers, header => header.Value.Any(value => value.Contains(temporary, StringComparison.Ordinal)));
        Assert.Equal(HttpStatusCode.OK, await Authenticate(temporary));
        Assert.Equal(HttpStatusCode.OK, await CallApi(temporary));
        Assert.Equal(HttpStatusCode.OK, await CallApi(temporary));
        using var realServerCreated = await CreateTemporary(serverToken);
        Assert.Equal(HttpStatusCode.OK, realServerCreated.StatusCode);
        using var realServerJson = JsonDocument.Parse(await realServerCreated.Content.ReadAsStringAsync());
        var realServerTemporary = realServerJson.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
        Assert.Equal(HttpStatusCode.OK, await CallApi(realServerTemporary));
        using (var realServerPayload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(realServerTemporary.Split('.')[1])))
        {
            Assert.Equal("server-issued-subject", realServerPayload.RootElement.GetProperty("sub").GetString());
            Assert.False(realServerPayload.RootElement.TryGetProperty("oi_au_id", out _));
            Assert.Equal("temporary_readonly", realServerPayload.RootElement.GetProperty("token_use").GetString());
        }
        using (var temporaryPayload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(temporary.Split('.')[1])))
        {
            Assert.True(JsonElement.DeepEquals(payload.RootElement.GetProperty("business"), temporaryPayload.RootElement.GetProperty("business")));
            Assert.True(JsonElement.DeepEquals(payload.RootElement.GetProperty("role"), temporaryPayload.RootElement.GetProperty("role")));
            Assert.True(temporaryPayload.RootElement.GetProperty("exp").GetInt64() <= payload.RootElement.GetProperty("exp").GetInt64());
            Assert.True(temporaryPayload.RootElement.GetProperty("exp").GetInt64() <= temporaryPayload.RootElement.GetProperty("iat").GetInt64() + 300);
        }
        async Task<HttpStatusCode> ApiOperation(string bearer, HttpMethod method, string path)
        {
            using var message = new HttpRequestMessage(method, path);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            using var result = await api.SendAsync(message);
            return result.StatusCode;
        }
        Assert.Equal(HttpStatusCode.Forbidden, await ApiOperation(temporary, HttpMethod.Post, "/api/test-write"));
        Assert.Equal(HttpStatusCode.Forbidden, await ApiOperation(temporary, HttpMethod.Get, "/api/unclassified"));
        Assert.Equal(HttpStatusCode.Forbidden, await ApiOperation(realServerTemporary, HttpMethod.Post, "/api/test-write"));
        Assert.Equal(HttpStatusCode.Forbidden, await ApiOperation(realServerTemporary, HttpMethod.Get, "/api/unclassified"));
        Assert.Equal(HttpStatusCode.OK, await ApiOperation(token, HttpMethod.Post, "/api/test-write"));
        Assert.Equal(HttpStatusCode.OK, await ApiOperation(token, HttpMethod.Get, "/api/unclassified"));
        using var noBearer = await CreateTemporary(null);
        using var chain = await CreateTemporary(temporary);
        using var empty = await CreateTemporary(token, "{\"scopes\":[]}");
        using var excessive = await CreateTemporary(token, "{\"scopes\":[\"other\"]}");
        using var forged = await CreateTemporary(CreateToken(signingKey: new RsaSecurityKey(otherRsa) { KeyId = jwt.Kid }));
        using var idToken = await CreateTemporary(CreateToken(type: "JWT"));
        using var wrongIssuer = await CreateTemporary(CreateToken(issuer: "http://other/"));
        using var expired = await CreateTemporary(CreateToken(expires: DateTime.UtcNow.AddSeconds(-1)));
        using var future = await CreateTemporary(CreateToken(notBefore: DateTime.UtcNow.AddMinutes(10), expires: DateTime.UtcNow.AddMinutes(15)));
        using var shortLived = await CreateTemporary(CreateToken(expires: DateTime.UtcNow.AddSeconds(30)));
        using var repeated = await CreateTemporary(token, "{\"scopes\":[\"api1\",\"api1\"]}");
        using var uppercase = await CreateTemporary(token, "{\"scopes\":[\"API1\"]}");
        using var defaultDisallowed = await CreateTemporary(CreateToken(scope: "api1 api2"));
        using var badAudience = await CreateTemporary(CreateToken(audience: "other-api"));
        using var missingScope = await CreateTemporary(CreateToken(scope: ""));
        using var subset = await CreateTemporary(CreateToken(scope: "api1 api2"), "{\"scopes\":[\"api1\"]}");
        Assert.Equal(HttpStatusCode.Unauthorized, noBearer.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, chain.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, excessive.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, idToken.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongIssuer.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, future.StatusCode);
        Assert.Equal(HttpStatusCode.OK, shortLived.StatusCode);
        using var shortLivedJson = JsonDocument.Parse(await shortLived.Content.ReadAsStringAsync());
        using var shortLivedPayload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(shortLivedJson.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!.Split('.')[1]));
        Assert.True(shortLivedPayload.RootElement.GetProperty("exp").GetInt64() <= DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeSeconds());
        Assert.DoesNotContain(temporary, await chain.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(token, await excessive.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, repeated.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, uppercase.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, defaultDisallowed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, badAudience.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, missingScope.StatusCode);
        Assert.Equal(HttpStatusCode.OK, subset.StatusCode);
        using var subsetJson = JsonDocument.Parse(await subset.Content.ReadAsStringAsync());
        Assert.Equal("api1", subsetJson.RootElement.GetProperty("data").GetProperty("scopes")[0].GetString());
        var subsetJwt = subsetJson.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
        using var subsetPayload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(subsetJwt.Split('.')[1]));
        Assert.Equal("api1", subsetPayload.RootElement.GetProperty("scope").GetString());
        Assert.Equal("api1", subsetPayload.RootElement.GetProperty("aud")[0].GetString());
        var sourceWithTwoAudiences = JsonNode.Parse(Base64UrlEncoder.DecodeBytes(token.Split('.')[1]))!.AsObject();
        sourceWithTwoAudiences["aud"] = new JsonArray("api1", "api2");
        sourceWithTwoAudiences["scope"] = "api1 api2";
        var multiToken = handler.CreateToken(sourceWithTwoAudiences.ToJsonString(), credentials, new Dictionary<string, object> { ["typ"] = "at+jwt" });
        builder.Configuration["TemporaryTokens:Scopes:api2:0"] = "api2";
        builder.Configuration["TemporaryTokens:Audiences:api2:Enabled"] = "true";
        builder.Configuration["TemporaryTokens:Audiences:api2:IndependentClaims:0"] = "role";
        builder.Configuration["TemporaryTokens:Audiences:api2:IndependentClaims:1"] = "business";
        // One JWT would present both scopes to both audiences, including
        // unapproved scope/audience combinations. Reject rather than widen.
        using var crossedTargets = await CreateTemporary(multiToken);
        Assert.Equal(HttpStatusCode.Forbidden, crossedTargets.StatusCode);
        builder.Configuration["TemporaryTokens:Scopes:api2:0"] = "api1";
        using var compatibleTargets = await CreateTemporary(multiToken);
        Assert.Equal(HttpStatusCode.OK, compatibleTargets.StatusCode);
        using var compatibleJson = JsonDocument.Parse(await compatibleTargets.Content.ReadAsStringAsync());
        using var compatiblePayload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(compatibleJson.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!.Split('.')[1]));
        Assert.Equal("api1", compatiblePayload.RootElement.GetProperty("aud")[0].GetString());
        Assert.Equal(1, compatiblePayload.RootElement.GetProperty("aud").GetArrayLength());
        builder.Configuration["TemporaryTokens:Audiences:api1:Enabled"] = "false";
        using var disabled = await CreateTemporary(token);
        Assert.Equal(HttpStatusCode.Forbidden, disabled.StatusCode);
        builder.Configuration["TemporaryTokens:Audiences:api1:Enabled"] = "true";
        async Task<(HttpStatusCode Status, string Content)> Exchange(string bearer)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/exchange");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            using var result = await client.SendAsync(request);
            return (result.StatusCode, await result.Content.ReadAsStringAsync());
        }
        var exchanged = await Exchange(token);
        Assert.Equal(HttpStatusCode.OK, exchanged.Status);
        Assert.Equal(HttpStatusCode.OK, await Authenticate(exchanged.Content));
        Assert.Equal(HttpStatusCode.OK, await CallApi(exchanged.Content));
        Assert.Equal(HttpStatusCode.Forbidden, (await Exchange(exchanged.Content)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Exchange(CreateToken(signingKey: new RsaSecurityKey(otherRsa) { KeyId = jwt.Kid }))).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Exchange(CreateToken(type: "JWT"))).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Exchange(CreateToken(audience: "other-api"))).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Exchange(CreateToken(scope: "other-scope"))).Status);
        var originalText = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(token.Split('.')[1]));
        var duplicated = originalText.Insert(1, "\"sub\":\"injected\",");
        var duplicatedToken = handler.CreateToken(duplicated, credentials, new Dictionary<string, object> { ["typ"] = "at+jwt" });
        Assert.Contains((await Exchange(duplicatedToken)).Status, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        var unknownClaim = originalText.Insert(1, "\"unreviewed_permissions\":{\"write\":true},");
        var unknownToken = handler.CreateToken(unknownClaim, credentials, new Dictionary<string, object> { ["typ"] = "at+jwt" });
        Assert.Equal(HttpStatusCode.Forbidden, (await Exchange(unknownToken)).Status);
        using var duplicateProduction = await CreateTemporary(duplicatedToken);
        using var unknownProduction = await CreateTemporary(unknownToken);
        var unknownPrivate = originalText.Insert(1, "\"oi_unrecognized\":\"value\",");
        using var unknownPrivateProduction = await CreateTemporary(handler.CreateToken(unknownPrivate, credentials, new Dictionary<string, object> { ["typ"] = "at+jwt" }));
        var confirmation = originalText.Insert(1, "\"cnf\":{\"jkt\":\"not-bearer\"},");
        using var confirmationProduction = await CreateTemporary(handler.CreateToken(confirmation, credentials, new Dictionary<string, object> { ["typ"] = "at+jwt" }));
        Assert.Contains(duplicateProduction.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        Assert.Equal(HttpStatusCode.Forbidden, unknownProduction.StatusCode);
        Assert.Contains(unknownPrivateProduction.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        Assert.Equal(HttpStatusCode.Unauthorized, confirmationProduction.StatusCode);
        using var trailingRequest = new HttpRequestMessage(HttpMethod.Post, "/api/temporary-tokens/");
        trailingRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", handler.CreateToken(confirmation, credentials, new Dictionary<string, object> { ["typ"] = "at+jwt" }));
        trailingRequest.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var trailingResponse = await client.SendAsync(trailingRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, trailingResponse.StatusCode);
        using var cookieRequest = new HttpRequestMessage(HttpMethod.Post, "/api/temporary-tokens");
        cookieRequest.Headers.Add("Cookie", ".AspNetCore.Identity.Application=not-a-bearer");
        cookieRequest.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var cookieResponse = await client.SendAsync(cookieRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, cookieResponse.StatusCode);
        // An authenticated query token must never authorize a different bearer
        // payload: the service may only consume the token validated from header.
        using var mixedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/temporary-tokens?access_token=" + Uri.EscapeDataString(token));
        mixedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(signingKey: new RsaSecurityKey(otherRsa) { KeyId = jwt.Kid }));
        mixedRequest.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var mixedResponse = await client.SendAsync(mixedRequest);
        Assert.NotEqual(HttpStatusCode.OK, mixedResponse.StatusCode);
        var exchangedServer = await Exchange(serverToken);
        Assert.Equal(HttpStatusCode.OK, exchangedServer.Status);
        using (var exchangedServerPayload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(exchangedServer.Content.Split('.')[1])))
        {
            Assert.False(exchangedServerPayload.RootElement.TryGetProperty("oi_au_id", out _));
        }
        var resignedPayload = BuildTestPayload(payload.RootElement);
        var resignedToken = handler.CreateToken(resignedPayload, credentials, new Dictionary<string, object> { ["typ"] = "at+jwt" });
        Assert.Equal(HttpStatusCode.OK, await Authenticate(resignedToken));
        Assert.Equal(HttpStatusCode.OK, await CallApi(resignedToken));
        using var resignedJson = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(resignedToken.Split('.')[1]));
        Assert.True(JsonElement.DeepEquals(payload.RootElement.GetProperty("business"), resignedJson.RootElement.GetProperty("business")));
        Assert.True(JsonElement.DeepEquals(payload.RootElement.GetProperty("role"), resignedJson.RootElement.GetProperty("role")));
        Assert.Equal("temporary_readonly", resignedJson.RootElement.GetProperty("token_use").GetString());
        Assert.NotEqual(payload.RootElement.GetProperty("jti").GetString(), resignedJson.RootElement.GetProperty("jti").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, await CallApi(CreateToken(type: "JWT")));
        Assert.Equal(HttpStatusCode.Unauthorized, await CallApi(CreateToken(issuer: "http://other/")));
        Assert.Equal(HttpStatusCode.Unauthorized, await CallApi(CreateToken(signingKey: new RsaSecurityKey(otherRsa) { KeyId = jwt.Kid })));
        Assert.Equal(HttpStatusCode.Unauthorized, await CallApi(CreateToken(expires: DateTime.UtcNow.AddMinutes(-10))));
        Assert.Equal(HttpStatusCode.Unauthorized, await CallApi(CreateToken(notBefore: DateTime.UtcNow.AddMinutes(10), expires: DateTime.UtcNow.AddMinutes(15))));
        Assert.Equal(HttpStatusCode.Unauthorized, await CallApi(CreateToken(audience: "other-api")));
        Assert.Equal(HttpStatusCode.Forbidden, await CallApi(CreateToken(scope: "other-scope")));
        await app.StopAsync();
    }

    // Isolated proof-of-concept only: caller must first authenticate the exact input token.
    private static string BuildTestPayload(JsonElement source)
    {
        RejectDuplicateProperties(source);
        if (source.TryGetProperty("token_use", out _))
        {
            throw new InvalidOperationException("Reserved claim");
        }
        if (source.EnumerateObject().Any(property =>
                (property.Name.StartsWith("oi_", StringComparison.OrdinalIgnoreCase) &&
                 (property.Name is not ("oi_tkn_id" or "oi_au_id" or "oi_prst") || property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()))) ||
                property.Name == "cnf"))
        {
            throw new InvalidOperationException("Unsupported protocol state");
        }

        string[] replaced = ["iss", "iat", "nbf", "exp", "jti", "scope", "aud", "oi_tkn_id", "oi_au_id"];
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in source.EnumerateObject())
            {
                if (!replaced.Contains(property.Name, StringComparer.Ordinal))
                {
                    property.WriteTo(writer);
                }
            }
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            writer.WriteString("iss", "http://localhost/");
            writer.WriteNumber("iat", now);
            writer.WriteNumber("nbf", now);
            writer.WriteNumber("exp", Math.Min(now + 300, source.GetProperty("exp").GetInt64()));
            writer.WriteString("jti", Guid.NewGuid().ToString());
            writer.WriteString("scope", "api1");
            writer.WriteString("aud", "api1");
            writer.WriteString("token_use", "temporary_readonly");
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidOperationException("Duplicate JSON property");
                }
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                RejectDuplicateProperties(item);
            }
        }
    }

    [Theory]
    [InlineData("{\"sub\":\"one\",\"sub\":\"two\"}")]
    [InlineData("{\"sub\":\"one\",\"s\\u0075b\":\"two\"}")]
    [InlineData("{\"nested\":[{\"flag\":true,\"flag\":false}]}")]
    [InlineData("{\"token_use\":\"other\"}")]
    [InlineData("{\"oi_tkn_id\":42}")]
    [InlineData("{\"OI_au_id\":\"old-authorization-id\"}")]
    [InlineData("{\"cnf\":{\"jkt\":\"proof-key\"}}")]
    public void PayloadPrototypeRejectsAmbiguousOrReservedFields(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Throws<InvalidOperationException>(() => BuildTestPayload(document.RootElement));
    }
}
