using System.Security.Claims;

namespace OpenIddictUI;

public static class Util
{
    public const string CaptchaId = "CaptchaId";
    public const string CaptchaImageKey = "Captcha:Image:{0}";
    public const string CaptchaSliderKey = "Captcha:Slider:{0}";
    public const string CaptchaSliderVerified = "Captcha:SliderVerified:{0}";
    public const string SmsRateLimit = "SMS:RateLimit:{0}";
    public const string RegisterCode = "Register:Code:{0}";
    public const string PhoneNumberTokenProvider = "PhoneNumberTokenProvider";
    public const string PurposeLogin = "Login";
    public const string PurposeRegister = "Register";
    public static string AuthorizePrefix = "/connect/authorize?";
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