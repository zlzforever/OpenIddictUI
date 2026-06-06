namespace OpenIddictUI.Options;

public class IdentityExtensionOptions
{
    /// <summary>软删除列名。null 表示不启用软删除。</summary>
    public string? SoftDeleteColumn { get; set; }

    /// <summary>Identity 表名映射。未配置则使用默认 asp_net_*。</summary>
    public IdentityTableNames Tables { get; set; } = new();
}

public class IdentityTableNames
{
    public string Users { get; set; } = "asp_net_users";
    public string Roles { get; set; } = "asp_net_roles";
    public string UserRoles { get; set; } = "asp_net_user_roles";
    public string UserClaims { get; set; } = "asp_net_user_claims";
    public string UserLogins { get; set; } = "asp_net_user_logins";
    public string RoleClaims { get; set; } = "asp_net_role_claims";
    public string UserTokens { get; set; } = "asp_net_user_tokens";
}
