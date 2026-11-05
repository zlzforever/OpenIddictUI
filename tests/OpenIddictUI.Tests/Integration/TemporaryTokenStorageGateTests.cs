using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation;
using OpenIddict.Validation.AspNetCore;
using OpenIddictUI.Controllers;
using OpenIddictUI.TemporaryTokens;

namespace OpenIddictUI.Tests.Integration;

public class TemporaryTokenStorageGateTests
{
    private sealed class GateDbContext(DbContextOptions<GateDbContext> options) : DbContext(options);

    [Fact]
    public async Task StoredTokenIdentifierIsNotTheJwtIdAndCannotBeReusedAsANewEntry()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=isolated-storage-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        string? authorizationId = null;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TemporaryTokens:Scopes:api1:0"] = "api1",
            ["TemporaryTokens:Audiences:api1:Enabled"] = "true",
            ["TemporaryTokens:Audiences:api1:IndependentClaims:0"] = "client_id"
        });
        builder.Services.AddSingleton<TemporaryTokenService>();
        builder.Services.AddControllers().AddApplicationPart(typeof(TemporaryTokensController).Assembly);
        builder.Services.AddAuthorization();
        builder.Services.AddDbContext<GateDbContext>(options => options.UseSqlite(connection).UseOpenIddict());
        builder.Services.AddAuthentication();
        builder.Services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<GateDbContext>())
            .AddServer(options =>
            {
                options.SetIssuer(new Uri("http://localhost/"));
                options.SetTokenEndpointUris("/connect/token");
                options.AllowClientCredentialsFlow();
                options.DisableAccessTokenEncryption();
                options.AddSigningCertificate(certificate);
                options.AddEphemeralEncryptionKey();
                options.UseAspNetCore().DisableTransportSecurityRequirement();
                options.AddEventHandler<OpenIddictServerEvents.HandleTokenRequestContext>(eventBuilder =>
                    eventBuilder.UseInlineHandler(context =>
                    {
                        var identity = new ClaimsIdentity("isolated");
                        identity.SetClaim(OpenIddictConstants.Claims.Subject, "subject");
                        identity.SetAuthorizationId(authorizationId);
                        identity.SetScopes("api1");
                        identity.SetResources("api1");
                        context.SignIn(new ClaimsPrincipal(identity));
                        return default;
                    }));
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.EnableTokenEntryValidation();
                options.EnableAuthorizationEntryValidation();
                options.UseAspNetCore();
                options.RejectTemporaryTokenProofOfPossession();
            });
        builder.Services.PostConfigure<OpenIddictValidationOptions>(options =>
        {
            options.TokenValidationParameters.ValidateLifetime = true;
            options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
        });
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapGet("/check", async (HttpContext context) =>
            (await context.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)).Succeeded
                ? Results.Ok() : Results.Unauthorized());
        app.MapPost("/exchange", async (HttpContext context) =>
        {
            var authentication = await context.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
            if (!authentication.Succeeded || !AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization, out var header) ||
                !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(header.Parameter))
            {
                return Results.Unauthorized();
            }
            var parts = header.Parameter.Split('.');
            if (parts.Length != 3)
            {
                return Results.StatusCode(403);
            }
            using var json = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[1]));
            if (!PrivateClaimsAreSupported(json.RootElement) || json.RootElement.TryGetProperty("token_use", out _))
            {
                return Results.StatusCode(403);
            }
            var payload = JsonNode.Parse(json.RootElement.GetRawText())!.AsObject();
            payload.Remove("oi_tkn_id");
            payload.Remove("oi_au_id");
            payload["jti"] = Guid.NewGuid().ToString();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            payload["iat"] = now;
            payload["nbf"] = now;
            payload["exp"] = Math.Min(now + 300, payload["exp"]!.GetValue<long>());
            payload["token_use"] = "temporary_readonly";
            var credentials = context.RequestServices.GetRequiredService<IOptions<OpenIddictServerOptions>>().Value.SigningCredentials.Single();
            return Results.Text(new JsonWebTokenHandler().CreateToken(payload.ToJsonString(), credentials, new Dictionary<string, object> { ["typ"] = "at+jwt" }));
        });
        await app.StartAsync();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<GateDbContext>().Database.EnsureCreatedAsync();
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var application = await manager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "test-client",
                ClientSecret = "isolated-only",
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.ClientCredentials
                }
            });
            var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            var authorization = await authorizations.CreateAsync(new OpenIddictAuthorizationDescriptor
            {
                ApplicationId = await manager.GetIdAsync(application),
                Subject = "subject",
                Status = OpenIddictConstants.Statuses.Valid,
                Type = OpenIddictConstants.AuthorizationTypes.Permanent
            });
            authorizationId = await authorizations.GetIdAsync(authorization);
        }
        var client = app.GetTestClient();
        using var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "test-client",
            ["client_secret"] = "isolated-only"
        }));
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, content);
        using var responseJson = JsonDocument.Parse(content);
        var source = responseJson.RootElement.GetProperty("access_token").GetString()!;
        using var secondResponse = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "test-client",
            ["client_secret"] = "isolated-only"
        }));
        secondResponse.EnsureSuccessStatusCode();
        using var secondJson = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        var secondSource = secondJson.RootElement.GetProperty("access_token").GetString()!;
        var body = JsonNode.Parse(Base64UrlEncoder.DecodeBytes(source.Split('.')[1]))!.AsObject();
        using (var sourceDocument = JsonDocument.Parse(body.ToJsonString()))
        {
            Assert.True(PrivateClaimsAreSupported(sourceDocument.RootElement));
        }
        Assert.NotNull(body["oi_tkn_id"]);
        Assert.Equal(authorizationId, body["oi_au_id"]!.GetValue<string>());
        var sourceEntryId = body["oi_tkn_id"]!.GetValue<string>();
        Assert.NotEqual(sourceEntryId, body["jti"]!.GetValue<string>());
        async Task<HttpStatusCode> Authenticate(string token)
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, "/check");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var result = await client.SendAsync(message);
            return result.StatusCode;
        }
        Assert.Equal(HttpStatusCode.OK, await Authenticate(source));
        Assert.Equal(HttpStatusCode.OK, await Authenticate(secondSource));
        using var productionRequest = new HttpRequestMessage(HttpMethod.Post, "/api/temporary-tokens");
        productionRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", source);
        productionRequest.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var productionResponse = await client.SendAsync(productionRequest);
        var productionContent = await productionResponse.Content.ReadAsStringAsync();
        Assert.True(productionResponse.IsSuccessStatusCode, productionContent);
        using var productionJson = JsonDocument.Parse(productionContent);
        var productionToken = productionJson.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
        using var productionPayload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(productionToken.Split('.')[1]));
        Assert.False(productionPayload.RootElement.TryGetProperty("oi_tkn_id", out _));
        Assert.False(productionPayload.RootElement.TryGetProperty("oi_au_id", out _));
        Assert.Equal(HttpStatusCode.OK, await Authenticate(productionToken));
        using var exchangeRequest = new HttpRequestMessage(HttpMethod.Post, "/exchange");
        exchangeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", source);
        using var exchangeResponse = await client.SendAsync(exchangeRequest);
        exchangeResponse.EnsureSuccessStatusCode();
        var exchanged = await exchangeResponse.Content.ReadAsStringAsync();
        using var exchangedBody = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(exchanged.Split('.')[1]));
        Assert.False(exchangedBody.RootElement.TryGetProperty("oi_tkn_id", out _));
        Assert.False(exchangedBody.RootElement.TryGetProperty("oi_au_id", out _));
        Assert.Equal("temporary_readonly", exchangedBody.RootElement.GetProperty("token_use").GetString());
        Assert.Equal("subject", exchangedBody.RootElement.GetProperty("sub").GetString());
        Assert.True(exchangedBody.RootElement.GetProperty("exp").GetInt64() <= body["exp"]!.GetValue<long>());
        Assert.True(exchangedBody.RootElement.GetProperty("exp").GetInt64() <= exchangedBody.RootElement.GetProperty("iat").GetInt64() + 300);
        Assert.Equal(HttpStatusCode.OK, await Authenticate(exchanged));
        var signingCredentials = app.Services.GetRequiredService<IOptions<OpenIddictServerOptions>>().Value.SigningCredentials.Single();
        var handler = new JsonWebTokenHandler();
        body["jti"] = Guid.NewGuid().ToString();
        var copiedEntryToken = handler.CreateToken(body.ToJsonString(), signingCredentials, new Dictionary<string, object> { ["typ"] = "at+jwt" });
        // A valid old entry can make a different JWT appear valid: never reuse its ID.
        Assert.Equal(HttpStatusCode.OK, await Authenticate(copiedEntryToken));
        body.Remove("oi_tkn_id");
        var noEntryToken = handler.CreateToken(body.ToJsonString(), signingCredentials, new Dictionary<string, object> { ["typ"] = "at+jwt" });
        // Entry validation is conditional on an identifier; absence is not proof of storage.
        Assert.Equal(HttpStatusCode.OK, await Authenticate(noEntryToken));
        string newEntryId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
            var entry = await manager.CreateAsync(new OpenIddictTokenDescriptor
            {
                AuthorizationId = authorizationId,
                Subject = "subject",
                Type = OpenIddictConstants.TokenTypeIdentifiers.AccessToken,
                Status = OpenIddictConstants.Statuses.Valid,
                CreationDate = DateTimeOffset.UtcNow,
                ExpirationDate = DateTimeOffset.FromUnixTimeSeconds(body["exp"]!.GetValue<long>())
            });
            newEntryId = (await manager.GetIdAsync(entry))!;
        }
        Assert.NotEqual(sourceEntryId, newEntryId);
        body["oi_tkn_id"] = newEntryId;
        body["token_use"] = "temporary_readonly";
        var newEntryToken = handler.CreateToken(body.ToJsonString(), signingCredentials, new Dictionary<string, object> { ["typ"] = "at+jwt" });
        Assert.Equal(HttpStatusCode.OK, await Authenticate(newEntryToken));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
            var entry = await manager.FindByIdAsync(sourceEntryId);
            Assert.NotNull(entry);
            Assert.True(await manager.TryRevokeAsync(entry));
        }
        Assert.Equal(HttpStatusCode.Unauthorized, await Authenticate(source));
        Assert.Equal(HttpStatusCode.Unauthorized, await Authenticate(copiedEntryToken));
        Assert.Equal(HttpStatusCode.OK, await Authenticate(noEntryToken));
        Assert.Equal(HttpStatusCode.OK, await Authenticate(newEntryToken));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            var authorization = await manager.FindByIdAsync(authorizationId!);
            Assert.NotNull(authorization);
            Assert.True(await manager.TryRevokeAsync(authorization));
        }
        Assert.Equal(HttpStatusCode.Unauthorized, await Authenticate(secondSource));
        Assert.Equal(HttpStatusCode.Unauthorized, await Authenticate(newEntryToken));
        Assert.Equal(HttpStatusCode.OK, await Authenticate(exchanged));
        Assert.Equal(HttpStatusCode.OK, await Authenticate(productionToken));
        // Authorization validation resolves the ID from the token entry, not from
        // the raw private claim alone. The unbacked token therefore remains valid.
        Assert.Equal(HttpStatusCode.OK, await Authenticate(noEntryToken));
        await app.StopAsync();
    }

    private static bool PrivateClaimsAreSupported(JsonElement payload)
    {
        foreach (var property in payload.EnumerateObject())
        {
            if (!property.Name.StartsWith("oi_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (property.Name is "oi_tkn_id" or "oi_au_id")
            {
                if (property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    return false;
                }
            }
            else if (property.Name == "oi_prst")
            {
                if (property.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    continue;
                }
                if (property.Value.ValueKind != JsonValueKind.Array ||
                    !property.Value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }
        return true;
    }

    [Theory]
    [InlineData("{\"oi_tkn_id\":42}")]
    [InlineData("{\"oi_au_id\":\"\"}")]
    [InlineData("{\"oi_prst\":[\"client\",false]}")]
    [InlineData("{\"oi_unrecognized\":\"value\"}")]
    [InlineData("{\"OI_tkn_id\":\"id\"}")]
    public void RejectsMalformedOrUnknownProtocolClaims(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.False(PrivateClaimsAreSupported(document.RootElement));
    }
}
