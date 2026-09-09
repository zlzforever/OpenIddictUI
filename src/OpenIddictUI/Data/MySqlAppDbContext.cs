using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.EntityFrameworkCore.Models;
using OpenIddictUI.Options;

namespace OpenIddictUI.Data;

public sealed class MySqlAppDbContext(
    DbContextOptions<MySqlAppDbContext> options,
    IOptions<IdentityExtensionOptions> identityOptions)
    : AppDbContext(options, identityOptions)
{
    protected override void AdjustModelCreating(ModelBuilder builder)
    {
        var applicationBuilder = builder.Entity<OpenIddictEntityFrameworkCoreApplication>();
        applicationBuilder.Property(x => x.Id).HasMaxLength(36);
        applicationBuilder.Property(x => x.ClientId).HasMaxLength(100);
        // BCrypt / SHA256 密文很短；如果用 JWK 可能长，2000 足够；**不要 TEXT*¸*
        applicationBuilder.Property(x => x.ClientSecret).HasMaxLength(2000);
        applicationBuilder.Property(x => x.DisplayName).HasMaxLength(500);
        applicationBuilder.Property(x => x.DisplayNames).HasColumnType("text");
        // JWKS 集合，公钥列表，可能很大，**只能保留 TEXT**
        applicationBuilder.Property(x => x.Permissions).HasMaxLength(2000);
        applicationBuilder.Property(x => x.PostLogoutRedirectUris).HasMaxLength(2000);
        applicationBuilder.Property(x => x.Properties).HasColumnType("text");
        applicationBuilder.Property(x => x.RedirectUris).HasMaxLength(2000);
        applicationBuilder.Property(x => x.Requirements).HasColumnType("text");
        applicationBuilder.Property(x => x.Settings).HasColumnType("text");
        applicationBuilder.Property(x => x.JsonWebKeySet).HasColumnType("text");

        var scopeBuilder = builder.Entity<OpenIddictEntityFrameworkCoreScope>();
        scopeBuilder.Property(x => x.Id).HasMaxLength(36);
        scopeBuilder.Property(x => x.Description).HasMaxLength(500);
        scopeBuilder.Property(x => x.Descriptions).HasColumnType("text");
        scopeBuilder.Property(x => x.DisplayName).HasMaxLength(500);
        scopeBuilder.Property(x => x.DisplayNames).HasColumnType("text");
        scopeBuilder.Property(x => x.Name).HasMaxLength(100);
        scopeBuilder.Property(x => x.Properties).HasColumnType("text");
        scopeBuilder.Property(x => x.Resources).HasMaxLength(2000);

        var authorizationBuilder = builder.Entity<OpenIddictEntityFrameworkCoreAuthorization>();
        authorizationBuilder.Property(x => x.Id).HasMaxLength(36);
        authorizationBuilder.Property("ApplicationId").HasMaxLength(36);
        authorizationBuilder.Property(x => x.Subject).HasMaxLength(255);
        authorizationBuilder.Property(x => x.Status).HasMaxLength(50);
        authorizationBuilder.Property(x => x.Properties).HasColumnType("text");
        authorizationBuilder.Property(x => x.Scopes).HasMaxLength(2000);

        var tokenBuilder = builder.Entity<OpenIddictEntityFrameworkCoreToken>();
        tokenBuilder.Property(x => x.Id).HasMaxLength(36);
        tokenBuilder.Property("ApplicationId").HasMaxLength(36);
        tokenBuilder.Property("AuthorizationId").HasMaxLength(36);
        tokenBuilder.Property(x => x.Subject).HasMaxLength(255);
        tokenBuilder.Property(x => x.Payload).HasColumnType("text");
        tokenBuilder.Property(x => x.Properties).HasMaxLength(2000);
    }
}