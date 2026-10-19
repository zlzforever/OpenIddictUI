using Microsoft.AspNetCore.Identity;

namespace OpenIddictUI.Identity;

public class User : IdentityUser
{
    /// <summary>软删除标记：true 表示已删除，由 AppDbContext 全局查询过滤器自动过滤。</summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// 业务完整姓名（如"江洋"）。列存在于外部用户管理系统维护的 asp_net_users.name 中，
    /// 本库 Identity 表 ExcludeFromMigrations，不产生迁移；签发 token 时映射为 family_name claim。
    /// </summary>
    public string? Name { get; set; }
}
