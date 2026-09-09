using MySqlConnector;
using Pomelo.Extensions.Caching.MySql;

namespace OpenIddictUI.Data;

public static class DatabaseStartup
{
    public static void InitializeMySql(MySqlCacheOptions options)
    {
        var queries = new MySqlQueries(options.SchemaName, options.TableName);
        using var conn = new MySqlConnection(options.ConnectionString);
        using var command = conn.CreateCommand();
        command.CommandText = queries.CreateTable;
        conn.Open();
        command.ExecuteNonQuery();
    }
}