using System.ComponentModel.DataAnnotations;

namespace OpenIddictUI.Controllers.Input;

public class LoginInput
{
    /// <summary>
    /// 
    /// </summary>
    [Required(ErrorMessage = "用户名不能为空"), StringLength(24)]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// 
    /// </summary>
    [Required(ErrorMessage = "密码不能为空"), StringLength(24)]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// 
    /// </summary>
    public bool RememberLogin { get; set; }

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

    /// <summary>
    /// 
    /// </summary>
    [StringLength(10)]
    public string? CaptchaCode { get; set; }
}