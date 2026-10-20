using System.Security.Claims;
using OpenIddict.Abstractions;
using OpenIddictUI.Identity;
using static OpenIddict.Abstractions.OpenIddictConstants;

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

    /// <summary>
    /// 归一化 name 并补充 family_name 姓名信息，在三处 token 签发点统一调用。
    /// 职责：
    ///   ① name 一律取用户名（UserName），消除同名重复 claim（Identity 长名 + 声明表短名），
    ///      避免 JWT 中 name 因同名合并变 JSON 数组、userinfo GetClaim 抛异常；
    ///   ② 补充姓名：微信等外部登录不产生 family_name/given_name claim，
    ///      将业务表 asp_net_users.name 完整姓名（如"江洋"）作为 family_name 写入 token，
    ///      使下游依赖姓名 claim 的后端能统一取值。
    /// 未写入 given_name：业务表无独立姓/名拆分列，遵循"完整名全部进 family_name"的约定。
    /// 全部使用 OpenIddict SetClaim（先移除全部同名 claim 再写入），框架语义天然保证单值。
    /// </summary>
    /// <param name="principal">待补全的用户主体，通常来自 CreateUserPrincipalAsync。</param>
    /// <param name="user">当前登录的用户实体，提供 UserName（用于 name 覆盖）与业务 Name 列（完整姓名）。</param>
    public static void NormalizeNameClaims(ClaimsPrincipal principal, User user)
    {
        // ① name 一律取用户名，保证单值：
        //    ClaimTypes.Name 是 Identity 固定生成的长名，出站映射后同样输出为 name，
        //    SetClaim 只会覆盖短名同名 claim，长名必须显式移除；
        //    声明表可能存在的短名 name 由 SetClaim 自动覆盖
        if (!string.IsNullOrWhiteSpace(user.UserName))
        {
            principal.RemoveClaims(ClaimTypes.Name);
            principal.SetClaim(Claims.Name, user.UserName);
        }

        // ② 业务表 name 列存在时写入 family_name；
        //    SetClaim 会先移除已有同名 claim（含声明表残留）再写入，天然单值
        if (!string.IsNullOrWhiteSpace(user.Name))
        {
            principal.SetClaim(Claims.FamilyName, user.Name);
        }
    }

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