using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration.Json;

namespace OpenIddictUI.Extensions;

public static class WebApplicationBuilderExtensions
{
    public static void AddEnvironmentSubstitutedAppSettings(this WebApplicationBuilder builder)
    {
        var env = builder.Environment;

// 1. 精准移除系统自动注册的 appsettings.json、appsettings.{Env}.json 两个Json源，其余全部保留
        var jsonSources = builder.Configuration.Sources
            .OfType<JsonConfigurationSource>()
            .Where(s => s.Path is "appsettings.json" || s.Path == $"appsettings.{env.EnvironmentName}.json")
            .ToList();
        foreach (var src in jsonSources)
        {
            builder.Configuration.Sources.Remove(src);
        }

// 2. 手动读原始json、替换${变量}
        var json = File.ReadAllText(Path.Combine(env.ContentRootPath, "appsettings.json"));
        json = Regex.Replace(json, @"\$\{(?<k>.*?)\}", m =>
        {
            var key = m.Groups["k"].Value.Trim();
            return Environment.GetEnvironmentVariable(key) ?? m.Value;
        });
// 替换后的主配置入库
        builder.Configuration.AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));

// 3. 环境分文件同样处理（可选，同逻辑）
        var envJsonPath = $"appsettings.{env.EnvironmentName}.json";
        if (File.Exists(envJsonPath))
        {
            string envJson = File.ReadAllText(envJsonPath);
            envJson = Regex.Replace(envJson, @"\$\{(?<k>.*?)\}",
                m => Environment.GetEnvironmentVariable(m.Groups["k"].Value.Trim()) ?? m.Value);
            builder.Configuration.AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(envJson)));
        }
    }
}