using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using MySqlConnector;

namespace OpenIddictUI.Data;

public static class DatabaseStartup
{
    private const string CacheProbeKey = "__openiddictui_startup_probe__";
    private static readonly Regex MySqlVersionPattern =
        new("^(?<major>\\d+)\\.(?<minor>\\d+)(?:\\.(?<patch>\\d+))?", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static async Task InitializeAsync(
        IServiceProvider services,
        DatabaseProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var cache = services.GetRequiredService<IDistributedCache>();
        if (provider == DatabaseProvider.Postgres)
        {
            await ProbeCacheAsync(cache, cancellationToken);
            return;
        }

        var settings = services.GetRequiredService<MySqlCacheSettings>();
        await InitializeMySqlAsync(settings, cache, cancellationToken);
    }

    internal static bool IsSupportedMySqlVersion(string? serverVersion, out Version? parsedVersion)
    {
        parsedVersion = null;
        if (string.IsNullOrWhiteSpace(serverVersion) ||
            serverVersion.Contains("mariadb", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var match = MySqlVersionPattern.Match(serverVersion.Trim());
        if (!match.Success ||
            !int.TryParse(match.Groups["major"].Value, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(match.Groups["minor"].Value, CultureInfo.InvariantCulture, out var minor))
        {
            return false;
        }

        var patch = match.Groups["patch"].Success &&
                    int.TryParse(match.Groups["patch"].Value, CultureInfo.InvariantCulture, out var parsedPatch)
            ? parsedPatch
            : 0;
        parsedVersion = new Version(major, minor, patch);
        return parsedVersion >= new Version(8, 0, 0);
    }

    private static async Task InitializeMySqlAsync(
        MySqlCacheSettings settings,
        IDistributedCache cache,
        CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var serverVersion = await ReadServerVersionAsync(connection, cancellationToken);
        if (!IsSupportedMySqlVersion(serverVersion, out var parsedVersion))
        {
            throw new InvalidOperationException(
                $"MySQL server version '{serverVersion}' is unsupported. MySQL 8.0 or newer is required.");
        }

        await EnsureCacheTableAsync(connection, settings, cancellationToken);
        await ProbeCacheAsync(cache, cancellationToken);
    }

    private static async Task<string> ReadServerVersionAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT VERSION();";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static async Task EnsureCacheTableAsync(
        MySqlConnection connection,
        MySqlCacheSettings settings,
        CancellationToken cancellationToken)
    {
        var qualifiedTableName =
            $"{QuoteIdentifier(settings.SchemaName)}.{QuoteIdentifier(settings.TableName)}";
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                CREATE TABLE IF NOT EXISTS {qualifiedTableName} (
                    `Id` varchar(449) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
                    `Value` longblob NOT NULL,
                    `ExpiresAtTime` datetime(6) NOT NULL,
                    `SlidingExpirationInSeconds` bigint NULL,
                    `AbsoluteExpiration` datetime(6) NULL,
                    PRIMARY KEY (`Id`),
                    KEY `ix_expires_at_time` (`ExpiresAtTime`)
                ) ENGINE=InnoDB;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await ValidateCacheColumnsAsync(connection, settings, cancellationToken);
        await ValidateCacheIndexesAsync(connection, settings, cancellationToken);
    }

    private static async Task ValidateCacheColumnsAsync(
        MySqlConnection connection,
        MySqlCacheSettings settings,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH, DATETIME_PRECISION, COLLATION_NAME
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table;
            """;
        command.Parameters.AddWithValue("@schema", settings.SchemaName);
        command.Parameters.AddWithValue("@table", settings.TableName);

        var columns = new Dictionary<string, CacheColumn>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns[reader.GetString(0)] = new CacheColumn(
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt64(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5));
        }

        RequireColumn(columns, "Id", "varchar", "NO", 449, null, "ascii_bin");
        RequireColumn(columns, "Value", "longblob", "NO", null, null, null);
        RequireColumn(columns, "ExpiresAtTime", "datetime", "NO", null, 6, null);
        RequireColumn(columns, "SlidingExpirationInSeconds", "bigint", "YES", null, null, null);
        RequireColumn(columns, "AbsoluteExpiration", "datetime", "YES", null, 6, null);
    }

    private static async Task ValidateCacheIndexesAsync(
        MySqlConnection connection,
        MySqlCacheSettings settings,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT INDEX_NAME, COLUMN_NAME, SEQ_IN_INDEX
            FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            ORDER BY INDEX_NAME, SEQ_IN_INDEX;
            """;
        command.Parameters.AddWithValue("@schema", settings.SchemaName);
        command.Parameters.AddWithValue("@table", settings.TableName);

        var indexes = new List<CacheIndex>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            indexes.Add(new CacheIndex(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        }

        if (!indexes.Any(index =>
                string.Equals(index.Name, "PRIMARY", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(index.Column, "Id", StringComparison.OrdinalIgnoreCase) &&
                index.Sequence == 1))
        {
            throw new InvalidOperationException(
                $"MySQL cache table '{settings.SchemaName}.{settings.TableName}' must have a primary key on 'Id'.");
        }

        if (!indexes.Any(index =>
                string.Equals(index.Column, "ExpiresAtTime", StringComparison.OrdinalIgnoreCase) &&
                index.Sequence == 1))
        {
            throw new InvalidOperationException(
                $"MySQL cache table '{settings.SchemaName}.{settings.TableName}' must have an index on 'ExpiresAtTime'.");
        }
    }

    private static void RequireColumn(
        IReadOnlyDictionary<string, CacheColumn> columns,
        string name,
        string dataType,
        string nullable,
        long? characterMaximumLength,
        int? datetimePrecision,
        string? collationName)
    {
        if (!columns.TryGetValue(name, out var column) ||
            !string.Equals(column.DataType, dataType, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(column.IsNullable, nullable, StringComparison.OrdinalIgnoreCase) ||
            (characterMaximumLength.HasValue && column.CharacterMaximumLength != characterMaximumLength) ||
            (datetimePrecision.HasValue && column.DateTimePrecision != datetimePrecision) ||
            (collationName is not null &&
             !string.Equals(column.CollationName, collationName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"MySQL cache table is missing or has an incompatible '{name}' column.");
        }
    }

    private static async Task ProbeCacheAsync(IDistributedCache cache, CancellationToken cancellationToken)
    {
        await cache.GetAsync(CacheProbeKey, cancellationToken);
    }

    private static string QuoteIdentifier(string identifier) => $"`{identifier.Replace("`", "``")}`";

    private sealed record CacheColumn(
        string DataType,
        string IsNullable,
        long? CharacterMaximumLength,
        int? DateTimePrecision,
        string? CollationName);

    private sealed record CacheIndex(string Name, string Column, int Sequence);
}
