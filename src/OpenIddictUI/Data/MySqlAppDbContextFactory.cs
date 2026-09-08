using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenIddictUI.Data;

public sealed class MySqlAppDbContextFactory : IDesignTimeDbContextFactory<MySqlAppDbContext>
{
    public MySqlAppDbContext CreateDbContext(string[] args)
    {
        var configuration = DatabaseDesignTimeConfiguration.Build();
        var connectionString = DatabaseDesignTimeConfiguration.GetConnectionString(
            configuration,
            "Server=localhost;Database=openiddictui;");
        var historyTable = DatabaseDesignTimeConfiguration.GetMigrationsHistoryTable(configuration);
        var identityOptions = DatabaseDesignTimeConfiguration.GetIdentityOptions(configuration);

        var options = new DbContextOptionsBuilder<MySqlAppDbContext>()
            .UseMySql(
                connectionString,
                new MySqlServerVersion(new Version(8, 0, 0)),
                mysql => mysql
                    .MigrationsHistoryTable(historyTable)
                    .MigrationsAssembly(typeof(MySqlAppDbContext).Assembly.GetName().Name))
            .Options;

        return new MySqlAppDbContext(
            options,
            Microsoft.Extensions.Options.Options.Create(identityOptions));
    }
}
