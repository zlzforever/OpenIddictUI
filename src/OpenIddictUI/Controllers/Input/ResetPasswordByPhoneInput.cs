using System.ComponentModel.DataAnnotations;

namespace OpenIddictUI.Controllers.Input;

public class ResetPasswordByPhoneInput
{
    /// <summary>
    /// 
    /// </summary>
    [Required, StringLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// 
    /// </summary>
    [Required, StringLength(8)]
    public string VerifyCode { get; set; } = string.Empty;

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
}