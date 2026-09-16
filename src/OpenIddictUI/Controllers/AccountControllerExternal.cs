using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using OpenIddictUI.Controllers.Input;
using OpenIddictUI.Extensions;
using OpenIddictUI.Identity;
using OpenIddictUI.Sms;

namespace OpenIddictUI.Controllers;

public partial class AccountController
{
    private const int MaxExternalBindingVerifyAttempts = 5;

    /// <summary>
    /// 发起第三方登录 — 仅允许配置中声明且已注册的外部 authentication scheme。
    /// </summary>
    [HttpGet("external-login")]
    public IActionResult ExternalLogin(
        [FromQuery, Required, StringLength(32)]
        string provider,
        [FromQuery, StringLength(2048)] string? returnUrl = null)
    {
        // returnUrl: https://xxx.com/openid/connect/authorize...
        if (!ModelState.IsValid || !IsValidReturnUrl(returnUrl))
        {
            return BadRequest(Errors.InvalidRequest);
        }

        var loginProvider = GetLoginProviderConfigured(provider);
        if (string.IsNullOrEmpty(loginProvider))
        {
            return BadRequest(Errors.InvalidRequest);
        }

        var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Account", new { returnUrl });
        if (string.IsNullOrEmpty(redirectUrl))
        {
            logger.LogError("External login callback URL could not be generated");
            return BadRequest(Errors.InvalidRequest);
        }

        var properties = signInManager.ConfigureExternalAuthenticationProperties(loginProvider, redirectUrl);
        properties.Items[Util.ExternalLoginReturnUrl] = returnUrl ?? string.Empty;

        return Challenge(properties, loginProvider);
    }

    /// <summary>
    /// 第三方登录回调 — 已绑定则登录，未绑定则进入手机号验证绑定流程。
    /// </summary>
    [HttpGet("external-login-callback")]
    public async Task<IActionResult> ExternalLoginCallback(
        [FromQuery, StringLength(2048)] string? returnUrl = null,
        [FromQuery(Name = "error"), StringLength(256)]
        string? remoteError = null)
    {
        if (!ModelState.IsValid || !IsValidReturnUrl(returnUrl))
        {
            return RedirectToLogin("invalid_return_url");
        }

        if (!string.IsNullOrWhiteSpace(remoteError))
        {
            logger.LogWarning("External login failed: {Error}", remoteError);
            return RedirectToLogin("external_login_failed", returnUrl);
        }

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            logger.LogWarning("External login callback did not contain valid LoginProvider login information");
            return RedirectToLogin("external_login_failed", returnUrl);
        }

        var configuredProvider = GetLoginProviderConfigured(info.LoginProvider);
        if (string.IsNullOrEmpty(configuredProvider))
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            logger.LogWarning("External login callback used an unsupported provider {Provider}", info.LoginProvider);
            return RedirectToLogin("external_login_failed", returnUrl);
        }

        // 外部登录回调的 returnUrl 由 OAuth state 中的 AuthenticationProperties 恢复。
        returnUrl = GetExternalReturnUrl(info, returnUrl);

        var signInResult = await signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: false);
        if (signInResult.Succeeded)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            logger.LogInformation("External login succeeded for {Provider}", info.LoginProvider);
            return Redirect(returnUrl ?? "/");
        }

        if (signInResult.IsLockedOut || signInResult.IsNotAllowed || signInResult.RequiresTwoFactor)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            logger.LogWarning(
                "External login rejected for {Provider}: locked out={LockedOut}, not allowed={NotAllowed}, two factor={TwoFactor}",
                info.LoginProvider, signInResult.IsLockedOut, signInResult.IsNotAllowed,
                signInResult.RequiresTwoFactor);
            return RedirectToLogin("external_login_failed", returnUrl);
        }

        var user = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (user != null)
        {
            await signInManager.SignInAsync(user, isPersistent: false, authenticationMethod: info.LoginProvider);
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            logger.LogInformation("External login succeeded for {Provider}", info.LoginProvider);
            return Redirect(returnUrl ?? "/");
        }

        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        if (string.IsNullOrWhiteSpace(info.ProviderKey))
        {
            logger.LogWarning("External login provider {Provider} cannot enter binding flow", info.LoginProvider);
            return RedirectToLogin("external_login_failed", returnUrl);
        }

        var ticketId = await CreateExternalBindingTicketAsync(info, returnUrl);
        if (ticketId == null)
        {
            logger.LogError("Could not create an external binding ticket for provider key {ProviderKey}",
                info.ProviderKey);
            return RedirectToLogin("external_login_failed", returnUrl);
        }

        SetExternalBindingCookie(ticketId);
        logger.LogInformation("External login requires phone verification before binding");
        return Redirect(BuildApplicationPath("account/bind-external"));
    }

    /// <summary>
    /// 检查当前浏览器是否持有有效的外部绑定票据。
    /// </summary>
    [HttpGet("external-binding/status")]
    public async Task<IActionResult> ExternalBindingStatus()
    {
        var binding = await GetExternalBindingTicketAsync();
        if (binding == null)
        {
            ClearExternalBindingCookie();
            return Ok(Errors.ExternalBindingExpiredResult);
        }

        return Ok(ApiResult.Ok());
    }

    private async Task<IActionResult> SendExternalBindingCode(string countryCode, string phoneNumber,
        string rateLimitKey)
    {
        var binding = await GetExternalBindingTicketAsync();
        if (binding == null)
        {
            return Ok(Errors.ExternalBindingExpiredResult);
        }

        var ticket = binding.Value.Ticket;
        if (!string.IsNullOrEmpty(ticket.PhoneNumber) &&
            !string.Equals(ticket.PhoneNumber, phoneNumber, StringComparison.OrdinalIgnoreCase))
        {
            return Ok(Errors.InvalidRequest);
        }

        // 绑定只能有一个, 如果手机号有多个，则不知道绑定给谁
        var user = await FindUniqueUserByPhoneAsync(phoneNumber);
        if (user == null)
        {
            // TODO: 有必要更新发送时间吗？
            // 对不存在或重复手机号保持模糊响应，避免通过绑定接口枚举账号。
            await smsManager.MarkRateLimitedAsync(rateLimitKey);
            return Ok(ApiResult.Ok("发送成功"));
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            // 不暴露手机号对应的账户状态；同时不为已锁定账户发送验证码。
            await smsManager.MarkRateLimitedAsync(rateLimitKey);
            return Ok(ApiResult.Ok("发送成功"));
        }

        var code = await userManager.GenerateUserTokenAsync(
            user, TokenOptions.DefaultPhoneProvider, Util.PurposeBindExternal);
        var sendResult = await smsManager.SendAsync(new SendCodeRequest(
            phoneNumber,
            countryCode,
            code,
            rateLimitKey,
            Util.PurposeBindExternal));

        if (sendResult == SmsSendStatus.RateLimited)
        {
            return Ok(ApiResult.Ok("发送成功"));
        }

        if (sendResult != SmsSendStatus.Sent)
        {
            return Ok(Errors.SendSmsFailedResult);
        }

        var updatedTicket = ticket with { PhoneNumber = phoneNumber };
        await SaveExternalBindingTicketAsync(binding.Value.Token, updatedTicket);

        return Ok(ApiResult.Ok("发送成功"));
    }

    /// <summary>
    /// 使用手机号短信验证码把当前外部身份绑定到已有本地用户。
    /// </summary>
    [HttpPost("external-binding")]
    [AutoValidateAntiforgeryToken]
    public async Task<IActionResult> BindExternal([FromBody] ExternalBindingInput model)
    {
        if (!ModelState.IsValid)
        {
            return Ok(ApiResult.Error(400, GetModelErrors()));
        }

        var binding = await GetExternalBindingTicketAsync();
        if (binding == null)
        {
            return Ok(Errors.ExternalBindingExpiredResult);
        }

        var ticket = binding.Value.Ticket;
        var phoneNumber = model.PhoneNumber.Trim();
        if (string.IsNullOrEmpty(ticket.PhoneNumber) ||
            !string.Equals(ticket.PhoneNumber, phoneNumber, StringComparison.OrdinalIgnoreCase))
        {
            return Ok(Errors.InvalidRequest);
        }

        if (ticket.FailedVerifyAttempts >= MaxExternalBindingVerifyAttempts)
        {
            await ClearExternalBindingTicketAsync(binding.Value.Token);
            return Ok(Errors.ExternalBindingAttemptsExceededResult);
        }

        var user = await FindUniqueUserByPhoneAsync(phoneNumber);
        if (user == null)
        {
            return await HandleExternalBindingFailureAsync(
                binding.Value.Token, ticket, "user_not_found");
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            // 绑定接口不暴露账户锁定状态，且锁定账户不能通过外部登录绕过锁定。
            return await HandleExternalBindingFailureAsync(
                binding.Value.Token, ticket, "user_locked");
        }

        var validCode = await userManager.VerifyUserTokenAsync(
            user, TokenOptions.DefaultPhoneProvider, Util.PurposeBindExternal, model.VerifyCode);
        if (!validCode)
        {
            return await HandleExternalBindingFailureAsync(
                binding.Value.Token, ticket, "invalid_code");
        }

        var boundUser = await userManager.FindByLoginAsync(ticket.LoginProvider, ticket.ProviderKey);
        if (boundUser != null && boundUser.Id != user.Id)
        {
            await ClearExternalBindingTicketAsync(binding.Value.Token);
            return Ok(Errors.ExternalAlreadyBoundResult);
        }

        if (boundUser == null)
        {
            var addLoginResult = await userManager.AddLoginAsync(user,
                new UserLoginInfo(ticket.LoginProvider, ticket.ProviderKey, ticket.ProviderDisplayName));
            if (!addLoginResult.Succeeded)
            {
                boundUser = await userManager.FindByLoginAsync(ticket.LoginProvider, ticket.ProviderKey);
                if (boundUser == null || boundUser.Id != user.Id)
                {
                    await ClearExternalBindingTicketAsync(binding.Value.Token);
                    logger.LogWarning("External binding failed: {Errors}",
                        string.Join(", ", addLoginResult.Errors.Select(error => error.Code)));
                    return Ok(boundUser == null
                        ? Errors.ExternalBindingFailedResult
                        : Errors.ExternalAlreadyBoundResult);
                }
            }
        }

        await ClearExternalBindingTicketAsync(binding.Value.Token);
        await signInManager.SignInAsync(user, isPersistent: false, authenticationMethod: ticket.LoginProvider);

        logger.LogInformation("External account bound to user {UserId}", user.Id);
        return Ok(ApiResult.Ok(data: new { location = ticket.ReturnUrl ?? "/" }));
    }

    private async Task<IActionResult> HandleExternalBindingFailureAsync(
        string token, ExternalBindingTicket ticket, string reason)
    {
        // 对外统一返回，具体失败原因只写入服务端日志，避免通过错误码枚举手机号状态。
        logger.LogWarning("External binding verification failed for {Provider}: {Reason}",
            ticket.LoginProvider, reason);

        // 安全评估备注（暂接受风险）：这里是“读取票据 -> 递增 -> 回写”的非原子更新。
        // 攻击者需要先完成未绑定的外部登录并通过发送短信前的滑块验证，取得短期绑定票据，
        // 然后可使用不同 IP 并发提交错误验证码，使多个请求都读到相同的 FailedVerifyAttempts，
        // 从而丢失部分递增并绕过“最多 5 次”的精确限制。当前风险由外部登录门槛、滑块验证、
        // 绑定票据 5 分钟 TTL 和短信发送限频降低；这不等同于原子计数，也不应替代统一错误响应。
        // 若后续需要严格执行尝试次数，改为原子计数或分布式锁，并按绑定票据而非仅按 IP 限制请求。
        var failedAttempts = ticket.FailedVerifyAttempts + 1;
        if (failedAttempts >= MaxExternalBindingVerifyAttempts)
        {
            await ClearExternalBindingTicketAsync(token);
            return Ok(Errors.ExternalBindingAttemptsExceededResult);
        }

        await SaveExternalBindingTicketAsync(
            token,
            ticket with { FailedVerifyAttempts = failedAttempts });
        return Ok(Errors.ExternalBindingFailedResult);
    }

    private string? GetExternalReturnUrl(ExternalLoginInfo info, string? returnUrl)
    {
        var candidate = returnUrl;
        if (string.IsNullOrWhiteSpace(candidate) &&
            info.AuthenticationProperties?.Items.TryGetValue(Util.ExternalLoginReturnUrl, out var savedReturnUrl) ==
            true)
        {
            candidate = savedReturnUrl;
        }

        return string.IsNullOrWhiteSpace(candidate) ? null : IsValidReturnUrl(candidate) ? candidate : null;
    }

    private async Task<string?> CreateExternalBindingTicketAsync(ExternalLoginInfo info, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(info.ProviderKey))
        {
            return null;
        }

        var ticketId = Guid.CreateVersion7().ToString("N");
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var ticket = new ExternalBindingTicket(
            info.LoginProvider,
            info.ProviderKey,
            info.ProviderDisplayName,
            returnUrl,
            expiresAt);

        var key = GetExternalBindingCacheKey(ticketId);
        await hybridCache.SetAsync(key, ticket, new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(5) });

        // await distributedCache.SetStringAsync(key,
        //     JsonSerializer.Serialize(ticket),
        //     new DistributedCacheEntryOptions { AbsoluteExpiration = expiresAt });

        return ticketId;
    }

    private async Task<(string Token, ExternalBindingTicket Ticket)?> GetExternalBindingTicketAsync()
    {
        var token = Request.Cookies[Util.ExternalBindingCookie];
        if (token == null)
        {
            return null;
        }

        var isValid = token is { Length: 32 } && token.All(Uri.IsHexDigit);
        if (!isValid)
        {
            return null;
        }

        var cacheKey = GetExternalBindingCacheKey(token);

        var ticket = await hybridCache.GetOnlyAsync<ExternalBindingTicket>(cacheKey);

        if (ticket != null && ticket.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return (token, ticket);
        }

        await hybridCache.RemoveAsync(cacheKey);
        return null;
    }

    private async Task SaveExternalBindingTicketAsync(string token, ExternalBindingTicket ticket)
    {
        await hybridCache.SetAsync(GetExternalBindingCacheKey(token), ticket,
            new HybridCacheEntryOptions { Expiration = ticket.ExpiresAt - DateTimeOffset.UtcNow });
    }

    private async Task ClearExternalBindingTicketAsync(string token)
    {
        await hybridCache.RemoveAsync(GetExternalBindingCacheKey(token));
        ClearExternalBindingCookie();
    }

    private static string GetExternalBindingCacheKey(string token) =>
        string.Format(Util.ExternalBindingTicket, token);

    private void SetExternalBindingCookie(string token)
    {
        Response.Cookies.Append(Util.ExternalBindingCookie, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Path = "/",
            MaxAge = TimeSpan.FromMinutes(5)
        });
    }

    private void ClearExternalBindingCookie()
    {
        Response.Cookies.Delete(Util.ExternalBindingCookie, new CookieOptions { Path = "/" });
    }

    private async Task<User?> FindUniqueUserByPhoneAsync(string phoneNumber)
    {
        var users = await userManager.Users
            .Where(user => user.PhoneNumber == phoneNumber)
            .Take(2)
            .ToListAsync();

        return users.Count == 1 ? users[0] : null;
    }
}
