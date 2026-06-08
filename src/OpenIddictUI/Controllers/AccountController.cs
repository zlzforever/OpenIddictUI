using Microsoft.AspNetCore.Antiforgery;
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
public class AccountController(
    SignInManager<User> signInManager,
    UserManager<User> userManager,
    HybridCache cache,
    IAntiforgery antiforgery,
    IPasswordValidator<User> passwordValidator,
    IOptions<OpenIddictUIOptions> options,
    ILogger<AccountController> logger) : Controller
{
    /// <summary>
    /// 密码登录 — 用户名 + 密码 + 验证码
    /// 成功返回 { location } → 前端跳转到 returnUrl（通常回到 /connect/authorize 继续 OAuth 流程）
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginInput model)
    {
        // ① CSRF 校验（X-XSRF-TOKEN header）
        if (!await ValidateCsrf())
        {
            return CsrfError();
        }

        // ② 模型验证（[Required] / [StringLength]）
        if (!ModelState.IsValid)
        {
            return Ok(ApiResult.Error(400, GetModelErrors()));
        }

        if (!IsValidReturnUrl(model.ReturnUrl))
        {
            return Ok(Errors.InvalidRequest);
        }

        // ③ 取消按钮 → 直接跳回
        if (model.Button != "login")
        {
            return Ok(ApiResult.Ok(data: new { location = model.ReturnUrl ?? "/" }));
        }

        // ④ 图形验证码（Dev 环境跳过）
        if (!await HttpContext.CheckCaptchaAsync(cache, model.CaptchaCode))
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
    public async Task<IActionResult> LoginBySms([FromBody] LoginByCodeInput model)
    {
        if (!await ValidateCsrf())
        {
            return CsrfError();
        }

        if (!ModelState.IsValid)
        {
            return Ok(ApiResult.Error(400, GetModelErrors()));
        }

        if (!IsValidReturnUrl(model.ReturnUrl))
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
    /// 发送短信验证码 — 支持三种场景（Login / ResetPassword / Register）
    /// Login: 用户必须存在，生成 phone token → VerifyUserTokenAsync("Login")
    /// ResetPassword: 用户必须存在，生成 phone token → VerifyUserTokenAsync("ResetPassword")
    /// Register: 用户必须不存在，生成随机 6 位码 → 存入 HybridCache TTL 5min → 注册时比对
    /// 限频：同一手机号 60s 内只能发一次（HybridCache 控制）
    /// </summary>
    [HttpPost("send-sms-code")]
    public async Task<IActionResult> SendCode([FromBody] SendCodeInput model)
    {
        if (!ModelState.IsValid)
        {
            return Ok(ApiResult.Error(400, GetModelErrors()));
        }

        // ① 限频检查：同一手机号 60s 内不发第二次
        var key = string.Format(Util.SmsRateLimit, model.PhoneNumber);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var cached = await cache.GetOrCreateAsync(
            key,
            _ => new ValueTask<long?>((long?)null),
            new HybridCacheEntryOptions { Flags = HybridCacheEntryFlags.DisableLocalCacheWrite }
        );
        if (cached.HasValue && now - cached.Value < 60)
        {
            return Ok(ApiResult.Ok("发送成功")); // 模糊响应防爆破
        }

        var scenario = model.Scenario;
        var smsSender = HttpContext.RequestServices.GetRequiredKeyedService<ISmsSender>(options.Value.SmsSender);
        // ② Register 场景：手机号不能已注册
        if (string.Equals(scenario, "Register", StringComparison.OrdinalIgnoreCase))
        {
            var existingUser = await userManager.Users.FirstOrDefaultAsync(u =>
                u.PhoneNumber == model.PhoneNumber);
            if (existingUser != null)
            {
                logger.LogWarning("SendCode(Register): phone already exists {PhoneNumber}", model.PhoneNumber);
                return Ok(ApiResult.Ok("发送成功")); // 模糊响应，不暴露用户存在
            }

            // 图形验证码 / 滑动验证码校验
            var captchaErr = await VerifyCaptchaOrSliderAsync(model.CaptchaCode);
            if (captchaErr != null)
            {
                logger.LogDebug("SendCode(Register): captcha/slider failed for {Phone}", model.PhoneNumber);
                return Ok(captchaErr);
            }

            // 生成随机 6 位验证码（没有用户实体，无法用 UserManager token provider）
            var registerCode = System.Security.Cryptography.RandomNumberGenerator
                .GetInt32(100000, 999999).ToString();

            // 发送短信
            try
            {
                await smsSender.SendAsync($"{model.CountryCode} {model.PhoneNumber}", registerCode);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SMS send failed: {PhoneNumber}", model.PhoneNumber);
                return Ok(Errors.SendSmsFailedResult);
            }

            // 存入 HybridCache：TTL 5 分钟，注册时比对
            await cache.SetAsync(string.Format(Util.RegisterCode, model.PhoneNumber), registerCode,
                new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(5) });

            // 记录发送时间戳 → 60s TTL
            await cache.SetAsync(key, now, new HybridCacheEntryOptions { Expiration = TimeSpan.FromSeconds(60) });

            logger.LogInformation("SendCode(Register): code sent to {PhoneNumber}", model.PhoneNumber);
            return Ok(ApiResult.Ok("发送成功"));
        }

        // ③ Login / ResetPassword 场景：用户必须存在
        var user = await userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == model.PhoneNumber);
        if (user == null)
        {
            logger.LogWarning("SendCode({Scenario}): user not found {PhoneNumber}", scenario, model.PhoneNumber);
            return Ok(ApiResult.Ok("发送成功")); // 模糊响应防用户枚举
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return Ok(Errors.UserLockedOutResult);
        }

        // ④ 验证码校验：有 CaptchaCode 验图形，无 CaptchaCode 验滑块
        var captchaError = await VerifyCaptchaOrSliderAsync(model.CaptchaCode, user);
        if (captchaError != null)
        {
            return Ok(captchaError);
        }

        // ⑤ 生成验证码
        var code = await userManager.GenerateUserTokenAsync(user, TokenOptions.DefaultPhoneProvider, scenario);
        // ⑥ 发送短信
        try
        {
            await smsSender.SendAsync($"{model.CountryCode} {model.PhoneNumber}", code);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SMS send failed: {PhoneNumber}", model.PhoneNumber);
            return Ok(Errors.SendSmsFailedResult);
        }

        // ⑦ 记录发送时间戳 → 60s TTL
        await cache.SetAsync(key, now, new HybridCacheEntryOptions { Expiration = TimeSpan.FromSeconds(60) });
        return Ok(ApiResult.Ok("发送成功"));
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

        if (!await HttpContext.CheckCaptchaAsync(cache, model.CaptchaCode))
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

    /// <summary>
    /// CSRF 手动验证 — [ValidateAntiForgeryToken] 在 AddControllers 中未注册过滤器，手动调用
    /// </summary>
    private async Task<bool> ValidateCsrf()
    {
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private bool IsValidReturnUrl(string? returnUrl)
    {
        return string.IsNullOrWhiteSpace(returnUrl) || returnUrl.StartsWith(Util.AuthorizePrefix);
    }

    private IActionResult CsrfError() => BadRequest(Errors.InvalidAntiForgery);

    /// <summary>
    /// 验证码校验统一入口：有 CaptchaCode 验图形，无 CaptchaCode 验滑块
    /// 返回 null 表示通过，返回 ApiResult 表示错误（调用方 return Ok(error)）
    /// </summary>
    private async Task<ApiResult?> VerifyCaptchaOrSliderAsync(string? captchaCode, User? user = null)
    {
        if (!string.IsNullOrEmpty(captchaCode))
        {
            if (!await HttpContext.CheckCaptchaAsync(cache, captchaCode))
            {
                if (user != null && userManager.SupportsUserLockout)
                {
                    var afResult = await userManager.AccessFailedAsync(user);
                    if (!afResult.Succeeded)
                    {
                        logger.LogWarning("Captcha: AccessFailed 失败 {User}, {Errors}", user.UserName,
                            string.Join(", ", afResult.Errors.Select(e => e.Description)));
                    }
                }

                return Errors.InvalidCaptcha;
            }

            return null;
        }

        // 无 CaptchaCode → 校验滑块
        var sliderId = Request.Cookies["SliderCaptchaId"];
        if (string.IsNullOrEmpty(sliderId))
        {
            return Errors.SliderRequired;
        }

        var verifiedKey = string.Format(Util.CaptchaSliderVerified, sliderId);
        var sliderPassed = await cache.GetOrCreateAsync(
            verifiedKey,
            _ => new ValueTask<bool?>((bool?)null),
            new HybridCacheEntryOptions { Flags = HybridCacheEntryFlags.DisableLocalCacheWrite });

        if (sliderPassed != true)
        {
            return Errors.SliderRequired;
        }

        await cache.RemoveAsync(verifiedKey);
        return null;
    }
}

// ↓↓ 输入模型 — 全部带 StringLength 防 DoS ↓↓