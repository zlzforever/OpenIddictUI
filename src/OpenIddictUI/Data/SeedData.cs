using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

// ReSharper disable UnusedAutoPropertyAccessor.Local
// ReSharper disable CollectionNeverUpdated.Local
// ReSharper disable CollectionNeverUpdated.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace OpenIddictUI.Data;

public static class SeedData
{
    public static async Task ApplyAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SeedData");

        await context.Database.MigrateAsync();
        logger.LogInformation("Database migration applied.");

        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>().Get<OpenIddictConfig>();
        if (config == null)
        {
            return;
        }

        var mgr = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var smgr = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        foreach (var s in config.Scopes)
        {
            if (await smgr.FindByNameAsync(s.Name) is null)
            {
                await smgr.CreateAsync(new OpenIddictScopeDescriptor
                {
                    Name = s.Name,
                    DisplayName = s.DisplayName,
                    Description = s.Description,
                    Resources = { s.Name }
                });
                logger.LogInformation("Seed: scope {Name} created", s.Name);
            }
        }

        foreach (var c in config.Clients)
        {
            if (await mgr.FindByClientIdAsync(c.ClientId) is not null)
            {
                logger.LogDebug("Seed: client {ClientId} already exists, skipping", c.ClientId);
                continue;
            }

            var permissions = new List<string>
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession
            };

            foreach (var gt in c.GrantTypes)
            {
                switch (gt)
                {
                    case "authorization_code":
                        permissions.Add(Permissions.GrantTypes.AuthorizationCode);
                        permissions.Add(Permissions.ResponseTypes.Code);
                        break;
                    case "password":
                        permissions.Add(Permissions.GrantTypes.Password);
                        break;
                    case "refresh_token":
                        permissions.Add(Permissions.GrantTypes.RefreshToken);
                        break;
                    default:
                        permissions.Add(Permissions.Prefixes.GrantType + gt);
                        break;
                }
            }

            foreach (var sc in c.Scopes)
            {
                switch (sc)
                {
                    case "openid":
                        permissions.Add("scp:openid");
                        break;
                    case "profile":
                        permissions.Add("scp:profile");
                        break;
                    case "email":
                        permissions.Add("scp:email");
                        break;
                    case "phone":
                        permissions.Add("scp:phone");
                        break;
                    case "address":
                        permissions.Add("scp:address");
                        break;
                    case "roles":
                        permissions.Add("scp:roles");
                        break;
                    default:
                        permissions.Add(Permissions.Prefixes.Scope + sc);
                        break;
                }
            }

            var requirements = new HashSet<string>();
            if (c.RequirePkce)
            {
                requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
            }

            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = c.ClientId,
                ClientSecret = c.ClientSecret,
                ClientType = c.ClientType,
                DisplayName = c.DisplayName,
                ConsentType = c.ConsentType,
                Settings =
                {
                    ["enabled"] = c.Enabled ? "true" : "false"
                }
            };

            if (!string.IsNullOrEmpty(c.ClientUrl))
            {
                descriptor.Settings["client_url"] = c.ClientUrl;
            }

            if (!string.IsNullOrEmpty(c.ClientLogoUrl))
            {
                descriptor.Settings["client_logo_url"] = c.ClientLogoUrl;
            }

            foreach (var uri in c.RedirectUris)
            {
                descriptor.RedirectUris.Add(new Uri(uri));
            }

            foreach (var uri in c.PostLogoutRedirectUris)
            {
                descriptor.PostLogoutRedirectUris.Add(new Uri(uri));
            }

            foreach (var perm in permissions)
            {
                descriptor.Permissions.Add(perm);
            }

            foreach (var req in requirements)
            {
                descriptor.Requirements.Add(req);
            }

            await mgr.CreateAsync(descriptor);
            logger.LogInformation("Seed: client {ClientId} created", c.ClientId);
        }

        logger.LogInformation("Seed data applied: {ScopeCount} scopes, {ClientCount} clients.",
            config.Scopes.Count, config.Clients.Count);
    }

    private class OpenIddictConfig
    {
        public List<ScopeConfig> Scopes { get; set; } = [];
        public List<ClientConfig> Clients { get; set; } = [];
    }

    private class ScopeConfig
    {
        public string Name { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
    }

    private class ClientConfig
    {
        public string ClientId { get; set; } = string.Empty;
        public string? ClientSecret { get; set; }
        public string? DisplayName { get; set; }
        public string? ConsentType { get; set; } = "implicit";
        public string ClientType { get; set; } = "confidential";
        public List<string> RedirectUris { get; set; } = [];
        public List<string> PostLogoutRedirectUris { get; set; } = [];
        public List<string> GrantTypes { get; set; } = [];
        public List<string> Scopes { get; set; } = [];
        public bool RequirePkce { get; set; } = true;
        public bool Enabled { get; set; } = true;
        public string? ClientUrl { get; set; }
        public string? ClientLogoUrl { get; set; }
    }
}