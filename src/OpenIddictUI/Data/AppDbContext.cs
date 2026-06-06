using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddictUI.Identity;
using OpenIddictUI.Options;

namespace OpenIddictUI.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, IOptions<IdentityExtensionOptions> identityOptions)
    : IdentityDbContext<User>(options)
{
    private readonly IdentityExtensionOptions _identityOptions = identityOptions.Value;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // 注册 OpenIddict 实体映射（Applications/Scopes/Authorizations/Tokens 表）
        builder.UseOpenIddict();

        // Identity 表由外部用户管理系统维护，不纳入 EF Migration
        ConfigureIdentityTables(builder);

        // 软删除：通过全局查询过滤器自动过滤 IsDeleted=true 的记录
        ConfigureSoftDelete(builder);

        // 所有表名、列名、索引名、外键名统一转为 lowercase_snake_case
        ApplySnakeCaseNaming(builder);
    }

    // 映射 Identity 表到可配置的表名，并排除出 Migration
    private void ConfigureIdentityTables(ModelBuilder builder)
    {
        var t = _identityOptions.Tables;
        builder.Entity<User>().ToTable(t.Users, tb => tb.ExcludeFromMigrations());
        builder.Entity<IdentityRole>().ToTable(t.Roles, tb => tb.ExcludeFromMigrations());
        builder.Entity<IdentityUserRole<string>>().ToTable(t.UserRoles, tb => tb.ExcludeFromMigrations());
        builder.Entity<IdentityUserClaim<string>>().ToTable(t.UserClaims, tb => tb.ExcludeFromMigrations());
        builder.Entity<IdentityUserLogin<string>>().ToTable(t.UserLogins, tb => tb.ExcludeFromMigrations());
        builder.Entity<IdentityRoleClaim<string>>().ToTable(t.RoleClaims, tb => tb.ExcludeFromMigrations());
        builder.Entity<IdentityUserToken<string>>().ToTable(t.UserTokens, tb => tb.ExcludeFromMigrations());
    }

    // 软删除列名来自 IdentityExtensionOptions.SoftDeleteColumn，为 null 则跳过
    // HasQueryFilter 使用实体属性名 IsDeleted（非数据库列名），EF Core 自动映射
    private void ConfigureSoftDelete(ModelBuilder builder)
    {
        var col = _identityOptions.SoftDeleteColumn;
        if (string.IsNullOrEmpty(col))
        {
            return;
        }

        builder.Entity<User>().Property(u => u.IsDeleted).HasColumnName(col);
        builder.Entity<User>().HasQueryFilter(u => !u.IsDeleted);
    }

    private static void ApplySnakeCaseNaming(ModelBuilder builder)
    {
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            var tableName = entity.GetTableName()!;
            if (tableName.StartsWith("OpenIddict"))
            {
                tableName = "openiddict_" + ToSnake(tableName["OpenIddict".Length..]);
            }
            else
            {
                tableName = ToSnake(tableName);
            }

            entity.SetTableName(tableName);

            foreach (var prop in entity.GetProperties())
                prop.SetColumnName(ToSnake(prop.GetColumnName()));

            foreach (var key in entity.GetKeys())
                key.SetName(ToSnake(key.GetName()!));

            foreach (var fk in entity.GetForeignKeys())
                fk.SetConstraintName(ToSnake(fk.GetConstraintName()!));

            foreach (var idx in entity.GetIndexes())
                idx.SetDatabaseName(ToSnake(idx.GetDatabaseName()!));
        }
    }

    private static string ToSnake(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }
        var result = string.Concat(input.Select((c, i) =>
            i > 0 && char.IsUpper(c) && !char.IsUpper(input[i - 1])
                ? "_" + char.ToLowerInvariant(c)
                : char.ToLowerInvariant(c).ToString()));
        return result.Replace("open_iddict_", "openiddict_");
    }
}
