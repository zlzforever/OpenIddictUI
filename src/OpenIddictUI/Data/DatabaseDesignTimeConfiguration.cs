using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using OpenIddictUI.Options;

namespace OpenIddictUI.Data;

internal static class DatabaseDesignTimeConfiguration
{
    private static readonly Regex EnvironmentVariablePattern =
        new(@"\$\{(?<key>.*?)\}", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IConfiguration Build()
    {
        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    public static string GetConnectionString(IConfiguration configuration, string fallback)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? fallback;
        return SubstituteEnvironmentVariables(connectionString);
    }

    public static string GetMigrationsHistoryTable(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration["OpenIddict:MigrationsHistoryTable"]
            ?? "openiddict_migrations_history";
    }

    public static IdentityExtensionOptions GetIdentityOptions(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetSection("IdentityExtension").Get<IdentityExtensionOptions>()
            ?? new IdentityExtensionOptions();
    }

    internal static string SubstituteEnvironmentVariables(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return EnvironmentVariablePattern.Replace(value, match =>
        {
            var key = match.Groups["key"].Value.Trim();
            return Environment.GetEnvironmentVariable(key) ?? match.Value;
        });
    }
}
