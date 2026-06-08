using Microsoft.Extensions.Caching.Hybrid;

namespace OpenIddictUI.Extensions;

public static class HttpContextExtensions
{
    /// <summary>
    /// 验证码校验 — Dev 环境下跳过，空值也放行（允许不填验证码）
    /// 校验通过后从 Session 中删除已使用的验证码（防止重复使用）
    /// </summary>
    public static async Task<bool> CheckCaptchaAsync(this HttpContext httpContext, HybridCache cache, string? captcha)
    {
        var env = httpContext.RequestServices.GetRequiredService<IHostEnvironment>();
        if (env.IsDevelopment())
        {
            return true;
        }
        var captchaId = httpContext.Request.Cookies[Util.CaptchaId];
        if (string.IsNullOrEmpty(captchaId))
        {
            captchaId = httpContext.Request.Headers["Z-CaptchaId"];
        }

        if (string.IsNullOrEmpty(captchaId))
        {
            return false;
        }

        var cacheKey = string.Format(Util.CaptchaImageKey, captchaId);
        var stored = await cache.GetOnlyAsync<string>(cacheKey);
        if (stored == null || !stored.Equals(captcha, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        await cache.RemoveAsync(cacheKey);
        return true;
    }
}