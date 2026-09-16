using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using OpenIddictUI.Controllers.Input;
using OpenIddictUI.Extensions;
using OpenIddictUI.Identity;
using OpenIddictUI.Options;
using OpenIddictUI.Sms;

namespace OpenIddictUI.Controllers;

/// <summary>
/// 账户管理 API — 登录、短信验证码、修改密码、退出
/// 所有返回统一 ApiResult 格式 { code, success, message, data }
/// </summary>
[AllowAnonymous]
[Route("account")]
public partial class AccountController(
    SignInManager<User> signInManager,
    UserManager<User> userManager,
    HybridCache hybridCache,
    IPasswordValidator<User> passwordValidator,
    IOptions<OpenIddictOptions> options,
    IOptions<GlobalOptions> globalOptions,
    ISmsManager smsManager,
    ILogger<AccountController> logger) : Controller
{
    /// <summary>
    /// 返回前端可用的登录方式。配置值按约定归一化为小写并去重；外部 provider 由其 authentication scheme 注册方提供。
    /// </summary>
    [HttpGet("providers")]
    public IActionResult Providers()
    {
        var providers = globalOptions.Value.AuthenticationSchemes;
        return Ok(ApiResult.Ok(data: providers));
    }

    /// <summary>
    /// 密码登录 — 用户名 + 密码 + 验证码
    /// 成功返回 { location } → 前端跳转到 returnUrl（通常回到 /connect/authorize 继续 OAuth 流程）
    /// </summary>
    [HttpPost("login")]
    [AutoValidateAntiforgeryToken]
    public async Task<IActionResult> Login([FromBody] LoginInput model)
    {
        // ② 模型验证（[Required] / [StringLength]）
        if (!ModelState.IsValid)
        {
            return Ok(ApiResult.Error(400, GetModelErrors()));
        }

        if (!IsValidReturnUrl(model.ReturnUrl))
        {
            return Ok(Errors.InvalidRequest);
        }

        var loginProvider = GetLoginProviderConfigured(Util.LoginProviderPassword);
        if (string.IsNullOrEmpty(loginProvider))
        {
            return Ok(Errors.InvalidRequest);
        }

        // ③ 取消按钮 → 直接跳回
        if (model.Button != "login")
        {
            return Ok(ApiResult.Ok(data: new { location = model.ReturnUrl ?? "/" }));
        }

        // ④ 图形验证码（Dev 环境跳过）
        if (!await HttpContext.CheckCaptchaAsync(hybridCache, model.CaptchaCode))
        {
            return Ok(Errors.InvalidCaptcha);
        }

        if (options.Value.ForcePasswordSecurityPolicy)
        {
            var passwordValidateResult =
                await passwordValidator.ValidateAsync(userManager, new User(), model.Password);
            if (!passwordValidateResult.Succeeded)
            {
                return Ok(Errors.PasswordValidateFailedResult);
            }
        }

        // ⑤ 查找用户（软删除自动过滤，由 AppDbContext Global Query Filter 处理）
        var user = await userManager.FindByNameAsync(model.Username);
        if (user == null)
        {
            return Ok(Errors.InvalidCredentialsResult);
        }

        // ⑥ 密码校验 + 登录（lockoutOnFailure=true 启用锁定）
        var result = await signInManager.PasswordSignInAsync(user, model.Password, model.RememberLogin, true);
        if (!result.Succeeded)
        {
            var msg = result.IsLockedOut ? "用户被锁定"
                : result.IsNotAllowed ? "用户被禁用"
                : "用户名或密码错误";
            var code = result.IsLockedOut ? Errors.UserLockedOut
                : result.IsNotAllowed ? Errors.UserNotAllowed
                : Errors.InvalidCredentials;
            return Ok(ApiResult.Error(code, msg));
        }

        logger.LogInformation("Login: success for {Username}", model.Username);

        // ⑦ 登录成功 → 返回跳转地址
        return Ok(ApiResult.Ok(data: new { location = model.ReturnUrl ?? "/" }));
    }

    /// <summary>
    /// 手机验证码登录 — 手机号 + 短信验证码
    /// 注意：验证码由 /account/sendCode 发送，用户手机接收后填入
    /// </summary>
    [HttpPost("login-by-sms")]
    [AutoValidateAntiforgeryToken]
    public async Task<IActionResult> LoginBySms([FromBody] LoginByCodeInput model)
    {
        if (!ModelState.IsValid)
        {
            return Ok(ApiResult.Error(400, GetModelErrors()));
        }

        if (!IsValidReturnUrl(model.ReturnUrl))
        {
            return Ok(Errors.InvalidRequest);
        }

        var loginProvider = GetLoginProviderConfigured(Util.LoginProviderSms);
        if (string.IsNullOrEmpty(loginProvider))
        {
            return Ok(Errors.InvalidRequest);
        }

        if (model.Button != "login")
        {
            return Ok(ApiResult.Ok(data: new { location = model.ReturnUrl ?? "/" }));
        }

        // ① 按手机号查找用户
        var user = await userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == model.PhoneNumber);
        if (user == null)
        {
            return Ok(Errors.UserNotExistResult);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return Ok(Errors.UserLockedOutResult);
        }

        // ② 验证短信验证码（TokenOptions.DefaultPhoneProvider + purpose "Login"）
        var isValid =
            await userManager.VerifyUserTokenAsync(user, TokenOptions.DefaultPhoneProvider, "Login", model.VerifyCode);
        if (!isValid)
        {
            logger.LogDebug(new EventId(2, "InvalidVerifyCode"), "User failed to provide the correct verify code.");

            if (userManager.SupportsUserLockout)
            {
                var afResult = await userManager.AccessFailedAsync(user);
                if (!afResult.Succeeded)
                {
                    logger.LogWarning("LoginByCode: AccessFailed 失败 {PhoneNumber}, {Errors}", model.PhoneNumber,
                        string.Join(", ", afResult.Errors.Select(e => e.Description)));
                }
            }

            return Ok(Errors.VerifyCodeLoginFailed);
        }

        var alwaysLockout =
            AppContext.TryGetSwitch("Microsoft.AspNetCore.Identity.CheckPasswordSignInAlwaysResetLockoutOnSuccess",
                out var enabled) && enabled;
        // Only reset the lockout when not in quirks mode if either TFA is not enabled or the client is remembered for TFA.
        if (alwaysLockout || !await signInManager.IsTwoFactorEnabledAsync(user) ||
            await signInManager.IsTwoFactorClientRememberedAsync(user))
        {
            var resetLockoutResult = await userManager.ResetAccessFailedCountAsync(user);
            if (!resetLockoutResult.Succeeded)
            {
                // ResetLockout got an unsuccessful result that could be caused by concurrency failures indicating an
                // attacker could be trying to bypass the MaxFailedAccessAttempts limit. Return the same failure we do
                // when failing to increment the lockout to avoid giving an attacker extra guesses at the password.
                return Ok(Errors.VerifyCodeLoginFailed);
            }
        }

        // ③ 登录 + 更新安全戳（使旧 token 失效）
        await signInManager.SignInAsync(user, true);
        await userManager.UpdateSecurityStampAsync(user);
        return Ok(ApiResult.Ok(data: new { location = model.ReturnUrl ?? "/" }));
    }

    /// <summary>
    /// 发送短信验证码 — 支持四种场景（Login / ResetPassword / Register / BindExternal）
    /// Login: 用户必须存在，生成 phone token → VerifyUserTokenAsync("Login")
    /// ResetPassword: 用户必须存在，生成 phone token → VerifyUserTokenAsync("ResetPassword")
    /// Register: 用户必须不存在，生成随机 6 位码 → 存入 HybridCache TTL 5min → 注册时比对
    /// 限频：同一手机号 60s 内只能发一次（HybridCache 控制）
    /// </summary>
    [HttpPost("send-sms-code")]
    [AutoValidateAntiforgeryToken]
    public async Task<IActionResult> SendCode([FromBody] SendCodeInput model)
    {
        if (!ModelState.IsValid)
        {
            return Ok(ApiResult.Error(400, GetModelErrors()));
        }

        var phoneNumber = model.PhoneNumber.Trim();
        var countryCode = string.IsNullOrWhiteSpace(model.CountryCode) ? "+86" : model.CountryCode.Trim();
        var scenario = NormalizeScenario(model.Scenario);
        if (scenario == null)
        {
            return Ok(Errors.InvalidRequest);
        }

        // ① 所有短信场景统一使用滑块验证
        var captchaError = await VerifyCaptchaAsync();
        if (captchaError != null)
        {
            return Ok(captchaError);
        }

        // ② Register 场景：手机号不能已注册
        var rateLimitKey = string.Format(Util.SmsRateLimit, phoneNumber);
        if (scenario == Util.PurposeRegister)
        {
            return await SendRegisterCode(scenario, countryCode, phoneNumber, rateLimitKey);
        }
        else if (scenario == Util.PurposeBindExternal)
        {
            return await SendExternalBindingCode(countryCode, phoneNumber, rateLimitKey);
        }

        // ③ Login / ResetPassword 场景：用户必须存在
        var user = await userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber);
        if (user == null)
        {
            logger.LogWarning("SendCode({Scenario}): user not found {PhoneNumber}", scenario, phoneNumber);
            return Ok(ApiResult.Ok("发送成功")); // 模糊响应防用户枚举
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            logger.LogWarning("SendCode({Scenario}): user is locked-out {UserId}", scenario, user.Id);
            return Ok(Errors.UserLockedOutResult);
        }

        // ③ 生成验证码
        var code = await userManager.GenerateUserTokenAsync(user, TokenOptions.DefaultPhoneProvider, scenario);
        var sendStatus = await smsManager.SendAsync(new SendCodeRequest(
            phoneNumber,
            countryCode,
            code,
            rateLimitKey,
            scenario));
        if (sendStatus == SmsSendStatus.RateLimited)
        {
            logger.LogWarning("SendCode({Scenario}): phone is too frequent {PhoneNumber}", scenario, phoneNumber);
            return Ok(ApiResult.Ok("发送成功"));
        }

        return Ok(sendStatus != SmsSendStatus.Sent ? Errors.SendSmsFailedResult : ApiResult.Ok("发送成功"));
    }

    private async Task<IActionResult> SendRegisterCode(string scenario, string countryCode, string phoneNumber,
        string rateLimitKey)
    {
        var existingUser = await userManager.Users.AnyAsync(u =>
            u.PhoneNumber == phoneNumber);
        if (existingUser)
        {
            logger.LogWarning("SendCode(Register): phone already exists {PhoneNumber}", phoneNumber);
            return Ok(ApiResult.Ok("发送成功")); // 模糊响应，不暴露用户存在
        }

        // 生成随机 6 位验证码（没有用户实体，无法用 UserManager token provider）
        var registerCode = System.Security.Cryptography.RandomNumberGenerator
            .GetInt32(100000, 999999).ToString();

        var sendResult = await smsManager.SendAsync(new SendCodeRequest(
            phoneNumber,
            countryCode,
            registerCode,
            rateLimitKey,
            scenario));

        switch (sendResult)
        {
            case SmsSendStatus.Sent:
            {
                await hybridCache.SetAsync(
                    string.Format(Util.RegisterCode, phoneNumber),
                    registerCode,
                    new HybridCacheEntryOptions
                    {
                        Expiration = TimeSpan.FromMinutes(5),
                        LocalCacheExpiration = TimeSpan.FromMinutes(5)
                    });

                return Ok(ApiResult.Ok("发送成功"));
            }
            case SmsSendStatus.RateLimited:
            {
                return Ok(Errors.SendSmsFailedResult);
            }
            default:
            {
                logger.LogWarning("SendCode(Register): phone is too frequent {PhoneNumber}", phoneNumber);
                return Ok(ApiResult.Ok("发送成功"));
            }
        }
    }

    /// <summary>
    /// 退出登录 — 清除 Identity cookie
    /// </summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        logger.LogInformation("Logout: signing out");
        await signInManager.SignOutAsync(); // 清除 idsrv cookie
        return Ok(ApiResult.Ok(data: new { location = "/logged-out" }));
    }

    /// <summary>
    /// 通过旧密码修改密码
    /// </summary>
    [HttpPost("change-password")]
    public async Task<IActionResult> ResetPasswordByOriginPassword([FromBody] ResetPasswordByOriginPasswordInput model)
    {
        if (!ModelState.IsValid)
        {
            return Ok(ApiResult.Error(400, GetModelErrors()));
        }

        if (!await HttpContext.CheckCaptchaAsync(hybridCache, model.CaptchaCode))
        {
            return Ok(Errors.InvalidCaptcha);
        }

        var user = await userManager.FindByNameAsync(model.UserName);
        if (user == null)
        {
            return Ok(Errors.UserNotExistResult);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return Ok(Errors.UserLockedOutResult);
        }

        // 验证旧密码正确（失败时累加锁定计数）
        var passwordOk = await userManager.CheckPasswordAsync(user, model.OldPassword);
        if (!passwordOk)
        {
            if (userManager.SupportsUserLockout)
            {
                var afResult = await userManager.AccessFailedAsync(user);
                if (!afResult.Succeeded)
                {
                    logger.LogWarning("重置密码: AccessFailed 失败 {User}, {Errors}", model.UserName,
                        string.Join(", ", afResult.Errors.Select(e => e.Description)));
                }
            }

            return Ok(Errors.IncorrectPassword);
        }

        // 旧密码正确 → 重置锁定计数（证明是账户持有者）
        if (userManager.SupportsUserLockout)
        {
            var resetResult = await userManager.ResetAccessFailedCountAsync(user);
            if (!resetResult.Succeeded)
            {
                logger.LogWarning("重置密码: ResetAccessFailedCount 失败 {User}, {Errors}", model.UserName,
                    string.Join(", ", resetResult.Errors.Select(e => e.Description)));
                return Ok(Errors.IncorrectPassword);
            }
        }

        // 生成重置 token → 重置密码（内置密码策略校验）
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, model.ConfirmNewPassword);
        if (result.Succeeded)
        {
            return Ok(ApiResult.Ok("修改成功"));
        }

        logger.LogError("用户 {User} 重置密码失败: {Errors}", model.UserName,
            string.Join(", ", result.Errors.Select(e => e.Description)));
        return Ok(Errors.ChangePasswordFailedResult);
    }

    /// <summary>
    /// 通过手机验证码重置密码
    /// </summary>
    [HttpPost("reset-password-by-sms")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordByPhoneInput model)
    {
        if (!ModelState.IsValid)
        {
            return Ok(ApiResult.Error(400, GetModelErrors()));
        }

        var user = await userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == model.PhoneNumber);
        if (user == null)
        {
            return Ok(Errors.UserNotExistResult);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return Ok(Errors.UserLockedOutResult);
        }

        // 验证短信验证码（失败时累加锁定计数）
        var isValid = await userManager.VerifyUserTokenAsync(user, TokenOptions.DefaultPhoneProvider, "ResetPassword",
            model.VerifyCode);
        if (!isValid)
        {
            if (userManager.SupportsUserLockout)
            {
                var afResult = await userManager.AccessFailedAsync(user);
                if (!afResult.Succeeded)
                {
                    logger.LogWarning("重置密码(手机): AccessFailed 失败 {PhoneNumber}, {Errors}", model.PhoneNumber,
                        string.Join(", ", afResult.Errors.Select(e => e.Description)));
                }
            }

            return Ok(Errors.VerifyCodeIncorrectResult);
        }

        // 验证码正确 → 重置锁定计数（证明是账户持有者）
        if (userManager.SupportsUserLockout)
        {
            var resetResult = await userManager.ResetAccessFailedCountAsync(user);
            if (!resetResult.Succeeded)
            {
                logger.LogWarning("重置密码(手机): ResetAccessFailedCount 失败 {PhoneNumber}, {Errors}", model.PhoneNumber,
                    string.Join(", ", resetResult.Errors.Select(e => e.Description)));
                return Ok(Errors.ResetPasswordByPhoneFailed);
            }
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, model.ConfirmNewPassword);
        if (result.Succeeded)
        {
            return Ok(ApiResult.Ok("修改成功"));
        }

        logger.LogError("用户 {PhoneNumber} 重置密码失败: {Errors}", model.PhoneNumber,
            string.Join(", ", result.Errors.Select(e => e.Description)));
        return Ok(Errors.ChangePasswordFailedResult);
    }

    private string GetModelErrors() =>
        string.Join("\n", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));

    private bool IsValidReturnUrl(string? returnUrl)
    {
        return string.IsNullOrWhiteSpace(returnUrl) || returnUrl.StartsWith(Util.AuthorizePrefix);
    }

    private RedirectResult RedirectToLogin(string error, string? returnUrl = null)
    {
        var pathBase = Util.BasePath;
        var loginUrl = string.IsNullOrEmpty(pathBase)
            ? "/account/login"
            : $"{pathBase}/account/login";
        var query = $"error={Uri.EscapeDataString(error)}";
        if (IsValidReturnUrl(returnUrl))
        {
            query += $"&returnUrl={Uri.EscapeDataString(returnUrl!)}";
        }

        return Redirect($"{loginUrl}?{query}");
    }

    /// <summary>
    /// 短信发送统一使用滑块验证。
    /// 验证失败不计入账户锁定次数，避免短信发送接口被用来锁定账户。
    /// 返回 null 表示通过，返回 ApiResult 表示错误（调用方 return Ok(error)）
    /// </summary>
    private async Task<ApiResult?> VerifyCaptchaAsync()
    {
        var sliderId = Request.Cookies[Util.CaptchaSliderCookie];
        if (string.IsNullOrEmpty(sliderId))
        {
            return Errors.SliderRequired;
        }

        var verifiedKey = string.Format(Util.CaptchaSliderVerified, sliderId);
        var sliderPassed = await hybridCache.GetOrCreateAsync(
            verifiedKey,
            _ => new ValueTask<bool?>((bool?)null),
            new HybridCacheEntryOptions { Flags = HybridCacheEntryFlags.DisableLocalCacheWrite });

        if (sliderPassed != true)
        {
            Response.Cookies.Delete(Util.CaptchaSliderCookie, new CookieOptions
            {
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/"
            });
            return Errors.SliderRequired;
        }

        await hybridCache.RemoveAsync(verifiedKey);
        Response.Cookies.Delete(Util.CaptchaSliderCookie, new CookieOptions
        {
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });
        return null;
    }

    private string? GetLoginProviderConfigured(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return null;
        }

        var configuredProvider = globalOptions.Value.AuthenticationSchemes.FirstOrDefault(configured =>
            string.Equals(configured, provider, StringComparison.OrdinalIgnoreCase));

        return configuredProvider;
    }

    private static string? NormalizeScenario(string? scenario)
    {
        if (string.IsNullOrWhiteSpace(scenario))
        {
            return null;
        }

        if (string.Equals(scenario, Util.PurposeLogin, StringComparison.OrdinalIgnoreCase))
        {
            return Util.PurposeLogin;
        }

        if (string.Equals(scenario, Util.PurposeResetPassword, StringComparison.OrdinalIgnoreCase))
        {
            return Util.PurposeResetPassword;
        }

        if (string.Equals(scenario, Util.PurposeRegister, StringComparison.OrdinalIgnoreCase))
        {
            return Util.PurposeRegister;
        }

        return string.Equals(scenario, Util.PurposeBindExternal, StringComparison.OrdinalIgnoreCase)
            ? Util.PurposeBindExternal
            : null;
    }

    private string BuildApplicationPath(string path)
    {
        return $"{Util.BasePath}/{path.TrimStart('/')}";
    }
}

// ↓↓ 输入模型 — 全部带 StringLength 防 DoS ↓↓