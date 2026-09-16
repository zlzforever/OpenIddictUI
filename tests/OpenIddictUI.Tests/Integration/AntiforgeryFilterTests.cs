using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenIddictUI.Controllers;

namespace OpenIddictUI.Tests.Integration;

public class AntiforgeryFilterTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder.UseSetting("OpenIddict:Issuer", "http://localhost"));
    private HttpClient client = null!;

    public Task InitializeAsync()
    {
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
    }

    [Fact]
    public async Task Login_WithoutAntiforgeryToken_ReturnsBadRequest()
    {
        var response = await client.PostAsJsonAsync("/account/login", new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_WithAntiforgeryToken_ReachesAction()
    {
        var tokenResponse = await client.GetAsync("/api/antiforgery/token");
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokenData = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = tokenData.GetProperty("token").GetString();
        token.Should().NotBeNullOrWhiteSpace();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/login")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("X-XSRF-TOKEN", token);

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var api = await response.Content.ReadFromJsonAsync<ApiResult>();
        api!.Code.Should().Be(400);
    }
}
