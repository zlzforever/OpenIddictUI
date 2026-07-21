using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration.Json;

namespace OpenIddictUI.Extensions;

public static class WebApplicationBuilderExtensions
{
    public static void AddSubstitution(this WebApplicationBuilder builder)
    {
        var env = builder.Environment;
        var replaceFiles = new Dictionary<string, int?>
        {
            { "appsettings.json", null },
            { $"appsettings.{env.EnvironmentName}.json", null }
        };
        var sources = builder.Configuration.Sources;
        for (var i = 0; i < builder.Configuration.Sources.Count; i++)
        {
            if (sources[i] is not FileConfigurationSource fcs)
            {
                continue;
            }

            if (string.IsNullOrEmpty(fcs.Path))
            {
                continue;
            }

            if (replaceFiles.ContainsKey(fcs.Path))
            {
                replaceFiles[fcs.Path] = i;
            }
        }

        string SubstituteEnv(string text)
        {
            return Regex.Replace(text, @"\$\{(?<k>.*?)\}", m =>
            {
                var key = m.Groups["k"].Value.Trim();
                return Environment.GetEnvironmentVariable(key) ?? m.Value;
            });
        }

        void ReplaceSource(ConfigurationManager configurationManager, int index, string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            var rawJson = SubstituteEnv(File.ReadAllText(path));
            var rawBytes = System.Text.Encoding.UTF8.GetBytes(rawJson);
            configurationManager.Sources[index] = new ReusableJsonStreamSource(rawBytes);
        }

        foreach (var kv in replaceFiles)
        {
            if (kv.Value == null)
            {
                continue;
            }

            ReplaceSource(builder.Configuration, kv.Value.Value, kv.Key);
        }
    }
}

/// <summary>
/// 每次 Build() 创建新 MemoryStream，避免 stream 被消费/关闭后无法重读
/// </summary>
file sealed class ReusableJsonStreamSource(byte[] rawBytes) : JsonStreamConfigurationSource
{
    public override IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        Stream = new MemoryStream(rawBytes);
        return base.Build(builder);
    }
}