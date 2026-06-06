namespace OpenIddictUI.Options;

public class OpenIddictUIOptions
{
    // public bool AutomaticRedirectAfterSignOut { get; set; }
    // public bool AllowLocalLogin { get; set; } = true;
    // public bool AllowRememberLogin { get; set; } = true;
    // public bool ShowLogoutPrompt { get; set; } = true;
    // public int RememberMeLoginDuration { get; set; }
    // public string? WindowsAuthenticationSchemeName { get; set; }
    public int? VerifyCodeLength { get; set; } = 6;
    public string SmsSender { get; set; } = "Console";
    public bool ForcePasswordSecurityPolicy { get; set; }
    public bool ForcePasswordGrantVerifyCaptcha { get; set; }

    /// <summary>EF Core 迁移历史表名，默认 "openiddict_migrations_history"。</summary>
    public string MigrationsHistoryTable { get; set; } = "openiddict_migrations_history";

    public string[] ForceEncryptPaths { get; set; } = ["/resetPwdByOriginPwd", "/resetPwd"];

    public int GetVerifyCodeLength()
    {
        // 必须小于等于9, 否则整数会溢出
        return VerifyCodeLength.HasValue
            ? VerifyCodeLength.Value <= 0 ? 4 : VerifyCodeLength.Value >= 9 ? 9 : VerifyCodeLength.Value
            : 6;
    }
}