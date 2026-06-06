using System.ComponentModel.DataAnnotations;

namespace OpenIddictUI.Controllers.Input;

public class SendCodeInput
{
    /// <summary>
    /// 
    /// </summary>
    [Required(ErrorMessage = "手机号不能为空"), StringLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// 
    /// </summary>
    [StringLength(5)]
    public string CountryCode { get; set; } = "+86";

    /// <summary>
    /// 
    /// </summary>
    [StringLength(20)]
    public string Scenario { get; set; } = "Login";

    /// <summary>
    /// 图形验证码。使用滑动验证码时可以传空（由前端 SliderCaptcha 兜底 + 服务端 60s 限频防刷）
    /// </summary>
    [StringLength(10)]
    public string? CaptchaCode { get; set; }
}