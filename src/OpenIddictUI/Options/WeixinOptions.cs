namespace OpenIddictUI.Options;

public class WeixinOptions
{
    public string AppId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>
    /// 微信授权回调的公网地址。配置后优先于当前请求地址，适用于 Dapr、网关等反向代理场景。
    /// </summary>
    public string? RedirectBaseUri { get; set; }

    /// <summary>
    /// 微信 OAuth 授权 scope 列表（如 snsapi_login、snsapi_userinfo）。
    /// 配置后完全覆盖第三方库默认 scope（清空后仅以此为准）；未配置时默认 snsapi_login（网站应用 PC 快速登录）。
    /// </summary>
    public List<string> Scopes { get; set; } = ["snsapi_login"];
}
