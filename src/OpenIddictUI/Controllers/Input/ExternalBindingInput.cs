using System.ComponentModel.DataAnnotations;

namespace OpenIddictUI.Controllers.Input;

public class ExternalBindingInput
{
    [Required(ErrorMessage = "手机号不能为空"), StringLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "请填写验证码"), StringLength(6)]
    public string VerifyCode { get; set; } = string.Empty;
}
