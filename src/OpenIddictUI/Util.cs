using System.Globalization;
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
    /// 姓名类 claim 归一化，在三处 token 签发点统一调用。
    /// 本系统 claim 语义约定：
    ///   preferred_username = 登录账号（user.UserName）；
    ///   name = 业务完整姓名（user.Name，如"江洋"）；
    ///   family_name / given_name = 姓、名拆分（由声明表 / 外部登录提供，本方法不改写其值）。
    /// 处理规则：
    ///   ① 账号转入 preferred_username：Identity 用 ClaimTypes.Name 长名承载用户名（账号），
    ///      而 name 的业务语义是完整姓名，长名必须移除，账号改用 preferred_username 承载；
    ///   ② 先读取声明表/外部登录产生的姓、名（长/短两种类型都可能存在），供 name 拼接兜底；
    ///   ③ 姓、名值保持原样，仅做类型归一：移除长名（ClaimTypes.GivenName / ClaimTypes.Surname，
    ///      出站映射后同样落到 given_name / family_name，不清理会同名合并成数组），
    ///      再以原值用 SetClaim 重写短名（先移除全部同名 claim 再写入，天然单值）；
    ///   ④ name 取值优先级：业务表 user.Name → 按系统 UI 语言拼接姓+名（中文姓在前，其余名在前）
    ///     → 登录账号（user.UserName）最终兜底 → 全部为空则不写入。
    /// </summary>
    /// <param name="principal">待补全的用户主体，通常来自 CreateUserPrincipalAsync。</param>
    /// <param name="user">当前登录的用户实体，提供 UserName（账号）与业务 Name 列（完整姓名）。</param>
    public static void NormalizeNameClaims(ClaimsPrincipal principal, User user)
    {
        // ① 账号转入 preferred_username：
        //    ClaimTypes.Name 是 Identity 固定生成的长名，出站映射后同样输出为 name，
        //    与业务完整姓名语义冲突，必须移除；短名 name 残留由后续 SetClaim 覆盖
        principal.RemoveClaims(ClaimTypes.Name);
        if (!string.IsNullOrWhiteSpace(user.UserName))
        {
            principal.SetClaim(Claims.PreferredUsername, user.UserName);
        }

        // ② 先读取已有姓、名（长/短名都尝试），用于 name 拼接兜底；
        //    注意必须在类型归一化之前读取
        var givenName = principal.FindFirst(Claims.GivenName)?.Value
            ?? principal.FindFirst(ClaimTypes.GivenName)?.Value;
        var familyName = principal.FindFirst(Claims.FamilyName)?.Value
            ?? principal.FindFirst(ClaimTypes.Surname)?.Value;

        // ③ 姓、名值不动，仅做长短名类型归一：长名不清理会与映射后的短名同名合并成数组；
        //    SetClaim 以原值覆盖短名残留，保证单值
        principal.RemoveClaims(ClaimTypes.GivenName);
        principal.RemoveClaims(ClaimTypes.Surname);
        if (!string.IsNullOrWhiteSpace(givenName))
        {
            principal.SetClaim(Claims.GivenName, givenName);
        }
        if (!string.IsNullOrWhiteSpace(familyName))
        {
            principal.SetClaim(Claims.FamilyName, familyName);
        }

        // ④ name：业务完整姓名优先；缺失时按系统 UI 语言拼姓+名；再缺失用账号兜底
        var name = user.Name;
        if (string.IsNullOrWhiteSpace(name) &&
            (!string.IsNullOrWhiteSpace(familyName) || !string.IsNullOrWhiteSpace(givenName)))
        {
            name = ConcatName(familyName, givenName);
        }
        name ??= user.UserName;
        if (!string.IsNullOrWhiteSpace(name))
        {
            principal.SetClaim(Claims.Name, name);
        }
    }

    /// <summary>
    /// 按系统当前 UI 语言拼接完整姓名。
    /// 中文（zh-*）习惯姓在前：family_name + given_name；其余语言习惯名在前：given_name + family_name。
    /// 文化取自服务器运行时的 CultureInfo.CurrentUICulture（未设置时回落到操作系统/进程默认语言）。
    /// </summary>
    /// <param name="familyName">姓（family_name），可为 null。</param>
    /// <param name="givenName">名（given_name），可为 null。</param>
    /// <returns>拼接后的完整姓名字符串；两侧都为 null 时为空字符串。</returns>
    private static string ConcatName(string? familyName, string? givenName)
    {
        // 取当前 UI 文化的两位语言码（如 zh-CN → zh）判断是否中文习惯
        var isChinese = string.Equals(
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase);

        return isChinese ? string.Concat(familyName, givenName) : string.Concat(givenName, familyName);
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