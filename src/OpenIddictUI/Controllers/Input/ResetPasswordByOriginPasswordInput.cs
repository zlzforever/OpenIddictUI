using System.ComponentModel.DataAnnotations;

namespace OpenIddictUI.Controllers.Input;

public class ResetPasswordByOriginPasswordInput
{
    /// <summary>
    /// 
    /// </summary>
    [Required, StringLength(50)]
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// 
    /// </summary>
    [Required, StringLength(32)]
    public string NewPassword { get; set; } = string.Empty;

    /// <summary>
    /// 
    /// </summary>
    [Required, StringLength(32), Compare(nameof(NewPassword))]
    public string ConfirmNewPassword { get; set; } = string.Empty;

    /// <summary>
    /// 
    /// </summary>
    [Required, StringLength(50)]
    public string OldPassword { get; set; } = string.Empty;

    /// <summary>
    /// 
    /// </summary>
    [StringLength(10)]
    public string CaptchaCode { get; set; } = string.Empty;
}