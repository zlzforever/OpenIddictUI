using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace OpenIddictUI.Data;

internal sealed class MySqlCacheSettings
{
    private static readonly Regex SafeIdentifier =
        new("^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private MySqlCacheSettings(
        string connectionString,
        string schemaName,
        string tableName,
        TimeSpan? expiredItemsDeletionInterval,
        TimeSpan defaultSlidingExpiration)
    {
        ConnectionString = connectionString;
        SchemaName = schemaName;
        TableName = tableName;
        ExpiredItemsDeletionInterval = expiredItemsDeletionInterval;
        DefaultSlidingExpiration = defaultSlidingExpiration;
    }

    public string ConnectionString { get; }

    public string SchemaName { get; }

    public string TableName { get; }

    public TimeSpan? ExpiredItemsDeletionInterval { get; }

    public TimeSpan DefaultSlidingExpiration { get; }

    public static MySqlCacheSettings FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Invalid configuration 'ConnectionStrings:DefaultConnection': a MySQL connection string is required.");
        }

        MySqlConnectionStringBuilder connectionBuilder;
        try
        {
            connectionBuilder = new MySqlConnectionStringBuilder(connectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new InvalidOperationException(
                "Invalid configuration 'ConnectionStrings:DefaultConnection': the MySQL connection string is invalid.",
                exception);
        }

        // Pomelo 的 upsert 查询使用用户变量计算过期时间。
        connectionBuilder.AllowUserVariables = true;
        var cacheConnectionString = connectionBuilder.ConnectionString;

        var databaseName = connectionBuilder.Database;
        ValidateIdentifier(databaseName, "ConnectionStrings:DefaultConnection database");

        var configuredSchema = configuration["MySqlCache:SchemaName"];
        var schemaName = configuredSchema is null ? databaseName : configuredSchema.Trim();
        ValidateIdentifier(schemaName, "MySqlCache:SchemaName");

        var configuredTable = configuration["MySqlCache:TableName"];
        var tableName = configuredTable is null
            ? "openiddict_cache_entries"
            : configuredTable.Trim();
        ValidateIdentifier(tableName, "MySqlCache:TableName");

        var expiredItemsDeletionInterval = ParseOptionalTimeSpan(
            configuration["MySqlCache:ExpiredItemsDeletionInterval"],
            "MySqlCache:ExpiredItemsDeletionInterval");
        var defaultSlidingExpiration = ParseTimeSpan(
            configuration["MySqlCache:DefaultSlidingExpiration"],
            "MySqlCache:DefaultSlidingExpiration",
            TimeSpan.FromMinutes(20));
        if (defaultSlidingExpiration <= TimeSpan.Zero)
        {
            throw InvalidValue(
                "MySqlCache:DefaultSlidingExpiration",
                configuration["MySqlCache:DefaultSlidingExpiration"],
                "the value must be positive");
        }

        return new MySqlCacheSettings(
            cacheConnectionString,
            schemaName,
            tableName,
            expiredItemsDeletionInterval,
            defaultSlidingExpiration);
    }

    internal static void ValidateIdentifier(string? value, string configurationKey)
    {
        if (string.IsNullOrWhiteSpace(value) || !SafeIdentifier.IsMatch(value))
        {
            throw InvalidValue(configurationKey, value, "the value must be a safe MySQL identifier");
        }
    }

    private static TimeSpan? ParseOptionalTimeSpan(string? value, string configurationKey)
    {
        if (value is null)
        {
            return null;
        }

        return ParseTimeSpan(value, configurationKey, null);
    }

    private static TimeSpan ParseTimeSpan(string? value, string configurationKey, TimeSpan? defaultValue)
    {
        if (value is null)
        {
            return defaultValue ?? throw InvalidValue(configurationKey, null, "a duration is required");
        }

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw InvalidValue(configurationKey, value, "the value must be a valid duration");
    }

    private static InvalidOperationException InvalidValue(
        string configurationKey,
        string? value,
        string reason)
    {
        return new InvalidOperationException(
            $"Invalid configuration '{configurationKey}' value '{value ?? "<missing>"}': {reason}.");
    }
}
