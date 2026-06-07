using Microsoft.Extensions.DependencyInjection;
using OpenIddictUI.Grants;

namespace OpenIddictUI.Extensions;

public static class GrantHandlerExtensions
{
    /// <summary>
    /// 注册 grant handler：keyed 用于 /connect/token 按 grant_type 查找，
    /// 非 keyed 用于 IEnumerable&lt;IGrantHandler&gt; 枚举 grant types 列表。
    /// </summary>
    public static IServiceCollection AddGrant<T>(this IServiceCollection services, string grantType)
        where T : class, IGrantHandler
    {
        services.AddKeyedSingleton<IGrantHandler, T>(grantType);
        services.AddSingleton<IGrantHandler>(sp => sp.GetRequiredKeyedService<IGrantHandler>(grantType));
        return services;
    }
}
