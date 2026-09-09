namespace OpenIddictUI.Data;

public enum DatabaseProvider
{
    Postgres,
    MySql
}

public static class DatabaseProviderResolver
{
    public const string ConfigurationKey = "Database";

    public static DatabaseProvider Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var value = configuration[ConfigurationKey];
        if (value is null)
        {
            return DatabaseProvider.Postgres;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "postgres" or "postgre" => DatabaseProvider.Postgres,
            "mysql" => DatabaseProvider.MySql,
            _ => throw new InvalidOperationException(
                $"Invalid configuration '{ConfigurationKey}' value '{value}'. " +
                "Expected 'postgres' or 'mysql'.")
        };
    }
}
