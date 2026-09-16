using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Distributed;

namespace OpenIddictUI.Weixin;

public class CachedStateDataFormat(IDistributedCache cache, string prefix = "wx")
    : ISecureDataFormat<AuthenticationProperties>
{
    private readonly TimeSpan _expire = TimeSpan.FromMinutes(5); // OAuth授权流程一般5分钟内完成

    public string Protect(AuthenticationProperties data)
    {
        return Protect(data, null);
    }

    public string Protect(AuthenticationProperties data, string? purpose)
    {
        // 生成简短随机key，作为state，很短，远小于128
        var key = $"{prefix}_{Guid.CreateVersion7():N}";
        if (!string.IsNullOrEmpty(data.RedirectUri))
        {
            data.RedirectUri = $"{Util.BasePath}{data.RedirectUri}";
        }

        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(data);
        cache.Set(key, json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _expire
        });

        return key;
    }

    public AuthenticationProperties? Unprotect(string? protectedText)
    {
        return Unprotect(protectedText, null);
    }

    public AuthenticationProperties? Unprotect(string? protectedText, string? purpose)
    {
        if (string.IsNullOrWhiteSpace(protectedText))
        {
            return null;
        }

        var json = cache.GetString(protectedText);
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        // 取出来立刻删除，一次性防重放
        cache.Remove(protectedText);
        return System.Text.Json.JsonSerializer.Deserialize<AuthenticationProperties>(json);
    }
}