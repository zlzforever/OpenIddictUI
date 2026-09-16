namespace OpenIddictUI.Options;

public class WeixinOptions
{
    public string AppId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>
    /// 微信授权回调的公网地址。配置后优先于当前请求地址，适用于 Dapr、网关等反向代理场景。
    /// </summary>
    public string? RedirectBaseUri { get; set; }
}
