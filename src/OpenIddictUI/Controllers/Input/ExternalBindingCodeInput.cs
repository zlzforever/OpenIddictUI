using System.ComponentModel.DataAnnotations;

namespace OpenIddictUI.Controllers.Input;

public class ExternalBindingCodeInput
{
    [Required(ErrorMessage = "手机号不能为空"), StringLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    [StringLength(5)]
    public string CountryCode { get; set; } = "+86";
}
