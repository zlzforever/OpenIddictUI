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

            using var stream =
                new MemoryStream(System.Text.Encoding.UTF8.GetBytes(SubstituteEnv(File.ReadAllText(path))));
            configurationManager.Sources[index] = new JsonStreamConfigurationSource
            {
                Stream = stream
            };
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