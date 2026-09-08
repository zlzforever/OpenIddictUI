using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenIddictUI.Data;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = DatabaseDesignTimeConfiguration.Build();
        var connectionString = DatabaseDesignTimeConfiguration.GetConnectionString(
            configuration,
            "Host=localhost;Database=openiddictui;");
        var historyTable = DatabaseDesignTimeConfiguration.GetMigrationsHistoryTable(configuration);
        var identityOptions = DatabaseDesignTimeConfiguration.GetIdentityOptions(configuration);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql
                    .MigrationsHistoryTable(historyTable)
                    .MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name))
            .Options;

        return new AppDbContext(
            options,
            Microsoft.Extensions.Options.Options.Create(identityOptions));
    }
}
