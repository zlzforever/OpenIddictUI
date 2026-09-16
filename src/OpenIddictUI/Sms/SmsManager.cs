using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using OpenIddictUI.Options;

namespace OpenIddictUI.Sms;

public interface ISmsManager
{
    Task<SmsSendStatus> SendAsync(SendCodeRequest request);

    Task MarkRateLimitedAsync(string rateLimitKey);
}

/// <summary>
/// 短信验证码的公共发送能力。场景本身负责身份校验和验证码生成，缓存策略通过请求按需传入。
/// </summary>
public sealed class SmsManager(
    HybridCache cache,
    IServiceProvider serviceProvider,
    IOptions<OpenIddictOptions> options,
    ILogger<SmsManager> logger) : ISmsManager
{
    /// <summary>
    /// 同一手机号发送验证码的最小间隔，单位秒。超过该间隔才允许再次发送。
    /// </summary>
    private const int RateLimitSeconds = 60;

    public async Task<SmsSendStatus> SendAsync(SendCodeRequest request)
    {
        var attemptId = Guid.CreateVersion7().ToString("N");

        try
        {
            // GetOrCreateAsync 对同一个 key 合并并发 factory，只有拿到自己 claim 的请求真正发送短信。
            var claim = await cache.GetOrCreateAsync(
                request.RateLimitKey,
                cancellationToken => SendAndClaimAsync(request, attemptId, cancellationToken),
                new HybridCacheEntryOptions
                {
                    Expiration = TimeSpan.FromSeconds(RateLimitSeconds),
                    LocalCacheExpiration = TimeSpan.FromSeconds(RateLimitSeconds)
                });

            return string.Equals(claim, attemptId, StringComparison.Ordinal)
                ? SmsSendStatus.Sent
                : SmsSendStatus.RateLimited;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SMS send failed for {Scenario}, {PhoneNumber}",
                request.Scenario, request.PhoneNumber);

            // factory 失败时 HybridCache 不应留下限频占位，允许用户在修复发送服务后重试。
            await cache.RemoveAsync(request.RateLimitKey);
            return SmsSendStatus.Failed;
        }
    }

    private async ValueTask<string> SendAndClaimAsync(
        SendCodeRequest request,
        string attemptId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var smsSender = serviceProvider.GetRequiredKeyedService<ISmsSender>(options.Value.SmsSender);
        await smsSender.SendAsync($"{request.CountryCode} {request.PhoneNumber}", request.Code);

        logger.LogInformation("SMS code sent for {Scenario}, {PhoneNumber}",
            request.Scenario, request.PhoneNumber);
        return attemptId;
    }

    public async Task MarkRateLimitedAsync(string rateLimitKey)
    {
        await cache.SetAsync(
            rateLimitKey,
            "rate-limited",
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromSeconds(RateLimitSeconds),
                LocalCacheExpiration = TimeSpan.FromSeconds(RateLimitSeconds)
            });
    }
}
