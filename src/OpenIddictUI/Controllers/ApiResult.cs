namespace OpenIddictUI.Controllers;

public class ApiResult
{
    public int Code { get; set; } = 200;
    public string? Message { get; set; }
    public object? Data { get; set; }
    public bool Success { get; set; } = true;

    public static ApiResult Ok(string? message = null, object? data = null)
        => new() { Message = message, Data = data };

    public static ApiResult Error(int code, string message)
        => new() { Code = code, Success = false, Message = message };
}

public static class Errors
{
    // 数值常量（必要时可直接引用）
    public const int TwoFactorNotSupported = 4001;
    public const int UserNotAllowed = 4002;
    public const int UserLockedOut = 4003;
    public const int InvalidCredentials = 4004;
    public const int NativeClientNotSupported = 4005;
    public const int InvalidReturnUrl = 4006;
    public const int ConsentInvalidSelection = 4007;
    public const int ConsentNoScopesMatching = 4008;
    public const int InvalidClientId = 4009;
    public const int NoConsentRequestMatching = 4010;
    public const int LoginFailed = 4011;
    public const int UserNotExist = 4012;
    public const int VerifyCodeExpired = 4013;
    public const int VerifyCodeIncorrect = 4014;
    public const int PasswordValidateFailed = 4015;
    public const int ChangePasswordFailed = 4016;
    public const int SendSmsFailed = 4017;
    public const int UserAlreadyExists = 4018;
    public const int SliderCaptchaExpired = 4019;
    public const int SliderCaptchaFailed = 4020;

    // 以下为"code + 固定 message"的快捷返回，调用方不再写 message
    public static ApiResult UserLockedOutResult => ApiResult.Error(UserLockedOut, "用户被锁定");
    public static ApiResult UserNotAllowedResult => ApiResult.Error(UserNotAllowed, "用户被禁用");
    public static ApiResult PasswordValidateFailedResult => ApiResult.Error(PasswordValidateFailed, "密码不符合安全要求，请先修改密码");
    public static ApiResult UserNotExistResult => ApiResult.Error(UserNotExist, "用户不存在");
    public static ApiResult InvalidCredentialsResult => ApiResult.Error(InvalidCredentials, "用户名或密码错误");

    // 之前共用 InvalidCredentials 的不同语义拆分
    public static ApiResult InvalidCaptcha => ApiResult.Error(4021, "验证码不正确");
    public static ApiResult IncorrectPassword => ApiResult.Error(4022, "密码不正确");
    public static ApiResult SliderRequired => ApiResult.Error(4023, "请先完成安全验证");

    // Consent
    public static ApiResult ConsentScopesExceed => ApiResult.Error(ConsentInvalidSelection, "Scopes exceed original request");
    public static ApiResult ConsentInvalid => ApiResult.Error(4024, "Invalid");
    public static ApiResult NoConsentRequestMatchingResult => ApiResult.Error(NoConsentRequestMatching, "Consent request expired");
    public static ApiResult ClientNotFoundResult => ApiResult.Error(InvalidClientId, "Invalid client");
    public static ApiResult ClientDisabled => ApiResult.Error(4025, "Client is disabled");

    // 其他
    public static ApiResult ChangePasswordFailedResult => ApiResult.Error(ChangePasswordFailed, "重置密码失败");
    public static ApiResult SendSmsFailedResult => ApiResult.Error(SendSmsFailed, "短信发送失败");
    public static ApiResult VerifyCodeLoginFailed => ApiResult.Error(VerifyCodeIncorrect, "登录失败");
    public static ApiResult VerifyCodeIncorrectResult => ApiResult.Error(VerifyCodeIncorrect, "验证码不正确");
    public static ApiResult ResetPasswordByPhoneFailed => ApiResult.Error(VerifyCodeIncorrect, "重置密码失败");
    public static ApiResult SliderCaptchaExpiredResult => ApiResult.Error(SliderCaptchaExpired, "验证已过期，请重试");
    public static ApiResult SliderCaptchaFailedResult => ApiResult.Error(SliderCaptchaFailed, "验证失败，请重试");
    public static ApiResult NotAuthenticated => ApiResult.Error(401, "Not authenticated");
    public static ApiResult InvalidRequest => ApiResult.Error(400, "请求不合法");
    public static ApiResult InvalidAntiForgery => ApiResult.Error(400, "Invalid anti-forgery token");
    public static ApiResult InvalidParams => ApiResult.Error(400, "参数错误");
}
