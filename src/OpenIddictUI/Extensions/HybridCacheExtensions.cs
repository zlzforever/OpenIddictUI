using Microsoft.Extensions.Caching.Hybrid;

namespace OpenIddictUI.Extensions;

public static class HybridCacheExtensions
{
    /// <summary>
    /// 纯读取缓存，不存在返回 null，不回源、不写入
    /// 专门用于验证码读取
    /// </summary>
    public static async Task<T?> GetOnlyAsync<T>(
        this HybridCache cache,
        string key,
        CancellationToken ct = default)
    {
        // 核心：工厂返回 null，不会写入缓存
        return await cache.GetOrCreateAsync<T?>(
            key,
            _ => ValueTask.FromResult<T?>(default),
            cancellationToken: ct);
    }
}