using System.Reflection;
using System.Security.Cryptography;

namespace OpenIddictUI;

/// <summary>
/// 构建指纹工具 — 在启动时输出版本与构建哈希，用于确认部署环境运行的程序与预期版本一致
/// 依赖：.NET 8+ SDK 在 git 仓库内构建时会自动将 Git 提交哈希写入 InformationalVersion
/// （即 SourceRevisionId，形如 "1.0.0+&lt;commit-sha&gt;"），无需额外构建脚本；
/// 二进制指纹（SHA256）作为与 Git 元数据无关的兜底标识，适用于脱离 git 目录构建的场景
/// </summary>
public static class BuildInfo
{
    /// <summary>
    /// 生成一行构建指纹描述，格式："version=x.y.z commit=&lt;git sha&gt; dll=&lt;sha256 前 12 位&gt;"
    /// </summary>
    /// <returns>可直接写入日志的指纹字符串；某项信息不可用时对应字段显示 unknown</returns>
    public static string Describe()
    {
        var version = "unknown";
        string? commit = null;
        var assembly = Assembly.GetEntryAssembly();
        if (assembly != null)
        {
            // InformationalVersion 形如 "1.0.0+<git 提交哈希>"，按 '+' 拆分出语义版本与源码 Revision
            var infoVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(infoVersion))
            {
                var plusIndex = infoVersion.IndexOf('+');
                if (plusIndex >= 0)
                {
                    version = infoVersion[..plusIndex];
                    commit = infoVersion[(plusIndex + 1)..];
                }
                else
                {
                    version = infoVersion;
                }
            }
        }

        return $"version={version} commit={commit ?? "unknown"} dll={ComputeBinaryHash(assembly)}";
    }

    /// <summary>
    /// 计算主程序集二进制文件的 SHA256 指纹（取前 12 位十六进制），作为与 Git 元数据无关的构建标识
    /// </summary>
    /// <param name="assembly">入口程序集；单文件发布时 Location 为空，此时退化为进程可执行文件路径</param>
    /// <returns>SHA256 前 12 位十六进制字符串；路径不可用或读取失败时返回 unknown</returns>
    private static string ComputeBinaryHash(Assembly? assembly)
    {
        try
        {
            var path = !string.IsNullOrEmpty(assembly?.Location)
                ? assembly.Location
                : Environment.ProcessPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return "unknown";
            }

            // SHA256.HashData 内部完成流式读取与哈希计算，无需手动缓冲
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream))[..12];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 指纹属于辅助诊断信息，IO 类异常不应阻断启动，降级为 unknown
            return "unknown";
        }
    }
}
