using System.Security.Claims;

namespace OpenIddictUI;

public static class Util
{
    public const string LoginProviderPassword = "password";
    public const string LoginProviderWeixin = "weixin";
    public const string LoginProviderSms = "sms";
    public const string ExternalLoginReturnUrl = "external_login_return_url";

    public const string CaptchaImageCookie = "openiddict-captcha-image";
    public const string CaptchaSliderCookie = "openiddict-captcha-slider";
    public const string CaptchaImageKey = "Captcha:Image:{0}";
    public const string CaptchaSliderKey = "Captcha:Slider:{0}";
    public const string CaptchaSliderVerified = "Captcha:SliderVerified:{0}";
    public const string SmsRateLimit = "SMS:RateLimit:v2:{0}";
    public const string RegisterCode = "Register:Code:{0}";
    public const string ExternalBindingTicket = "ExternalBinding:Ticket:{0}";
    public const string ExternalBindingCookie = "external-binding";
    public const string PhoneNumberTokenProvider = "PhoneNumberTokenProvider";
    public const string PurposeLogin = "Login";
    public const string PurposeRegister = "Register";
    public const string PurposeResetPassword = "ResetPassword";
    public const string PurposeBindExternal = "BindExternal";
    public static string AuthorizePrefix = "/connect/authorize?";
    public static string? BasePath;

    public static IServiceProvider? ServiceProvider;

    public static readonly Dictionary<string, string> JwtClaimMappings = new()
    {
        { ClaimTypes.NameIdentifier, "sub" },
        { ClaimTypes.Name, "name" },
        { ClaimTypes.Email, "email" },
        { ClaimTypes.Role, "role" },
        { ClaimTypes.GivenName, "given_name" },
        { ClaimTypes.Surname, "family_name" },
        { ClaimTypes.MobilePhone, "phone" },
        { "iat", "iat" },
        { "iss", "iss" },
        { "aud", "aud" }
    };
}