using System.ComponentModel.DataAnnotations;

namespace OpenIddictUI.Controllers.Input;

public class LoginByCodeInput
{
    /// <summary>
    /// 
    /// </summary>
    [Required(ErrorMessage = "手机号不能为空"), StringLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// 
    /// </summary>
    [Required(ErrorMessage = "请填写验证码"), StringLength(6)]
    public string VerifyCode { get; set; } = string.Empty;

    /// <summary>
    /// 
    /// </summary>
    [StringLength(2048)]
    public string? ReturnUrl { get; set; }

    /// <summary>
    /// 
    /// </summary>
    [StringLength(10)]
    public string Button { get; set; } = "login";
}