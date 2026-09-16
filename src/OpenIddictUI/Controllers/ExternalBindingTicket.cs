namespace OpenIddictUI.Controllers;

/// <summary>
/// 外部身份未绑定时的短期服务端票据。外部身份不放入前端 URL 或请求体。
/// </summary>
internal sealed record ExternalBindingTicket(
    string LoginProvider,
    string ProviderKey,
    string? ProviderDisplayName,
    string? ReturnUrl,
    DateTimeOffset ExpiresAt,
    string? PhoneNumber = null,
    int FailedVerifyAttempts = 0);
