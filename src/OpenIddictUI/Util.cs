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

    /// <summary>出站映射后都指向 name 的两类 claim 类型（Identity 长名 + 声明表可能存的 OpenIddict 短名）。</summary>
    private static readonly string[] NameClaimTypes =
        [ClaimTypes.Name, Claims.Name];

    /// <summary>
    /// 将 name claim 归一化为用户名：删除全部 name 候选 claim（Identity 长名 + 声明表短名），
    /// 仅保留一条值为用户名的 claim。
    /// 背景：CreateUserPrincipalAsync 会由 Identity 自动生成 ClaimTypes.Name=用户名；
    /// 而用户声明表(asp_net_user_claims)中若还存有 name 类型的昵称，
    /// 两条 claim 出站映射后同名为 name，OpenIddict（与 JwtSecurityTokenHandler 行为一致）
    /// 会把多个同名 claim 合并为 JSON 数组写入 token，导致客户端按字符串读取失败。
    /// 约定：无条件以 UserName 为准；昵称等非用户名的展示信息由声明表行删除或改用其他 claim 类型承载。
    /// </summary>
    /// <param name="principal">待归一化的用户主体，通常来自 CreateUserPrincipalAsync。</param>
    /// <param name="userName">当前登录用户的用户名（即 UserName 字段，可为 null），作为 name claim 的唯一取值来源。</param>
    private static void NormalizeNameClaim(ClaimsPrincipal principal, string? userName)
    {
        // 用户名为空时无法确定 name 取值，保持原状以免丢失信息
        if (string.IsNullOrWhiteSpace(userName))
        {
            return;
        }

        // 移除所有出站后为 name 的候选 claim（长/短两种类型），避免同名 claim 合并为数组
        foreach (var type in NameClaimTypes)
        {
            principal.RemoveClaims(type);
        }

        // 仅保留一条单值的 name claim
        // 目的地由调用方后续的 SetDestinations 统一设置（name 属于 Access + Identity token）
        principal.Identities.FirstOrDefault()?.AddClaim(new Claim(Claims.Name, userName));
    }

    /// <summary>
    /// 归一化 name 并补充 family_name 姓名信息，在三处 token 签发点统一调用。
    /// 职责：
    ///   ① name 一律取用户名（UserName），删除声明表昵称等重复 claim，避免 JWT 中 name 变 JSON 数组；
    ///   ② 补充姓名：微信等外部登录不产生 family_name/given_name claim，
    ///      将业务表 asp_net_users.name 完整姓名（如"江洋"）作为 family_name 写入 token，
    ///      使下游依赖姓名 claim 的后端能统一取值。
    /// 未写入 given_name：业务表无独立姓/名拆分列，遵循"完整名全部进 family_name"的约定。
    /// </summary>
    /// <param name="principal">待补全的用户主体，通常来自 CreateUserPrincipalAsync。</param>
    /// <param name="user">当前登录的用户实体，提供 UserName（用于 name 去重）与业务 Name 列（完整姓名）。</param>
    public static void NormalizeNameClaims(ClaimsPrincipal principal, User user)
    {
        // ① name 去重，保证 token 中 name 是单值字符串
        NormalizeNameClaim(principal, user.UserName);

        // ② 业务表 name 列存在时，写入 family_name claim（目的地同样由调用方 SetDestinations 统一设置）
        if (!string.IsNullOrWhiteSpace(user.Name))
        {
            principal.Identities.FirstOrDefault()?.AddClaim(new Claim(Claims.FamilyName, user.Name));
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