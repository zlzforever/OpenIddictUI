using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenIddictUI.Data;
using OpenIddictUI.Options;

namespace OpenIddictUI.Tests;

public class DatabaseProviderTests
{
    [Theory]
    [InlineData("postgres")]
    [InlineData("postgre")]
    [InlineData(" POSTGRES ")]
    [InlineData(" PostGre ")]
    public async Task PostgreSqlAliases_UsePostgreSqlProvider(string value)
    {
        await using var app = Program.CreateWebApplication([$"--Database={value}"]);
        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        context.Database.ProviderName.Should().Contain("Npgsql");
    }

    [Fact]
    public async Task MissingDatabase_UsesPostgreSqlProvider()
    {
        await using var app = Program.CreateWebApplication([]);
        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        context.Database.ProviderName.Should().Contain("Npgsql");
    }

    [Theory]
    [InlineData("mysql")]
    [InlineData(" MYSQL ")]
    [InlineData("MySql")]
    public async Task MySqlAliases_UseMySqlProvider(string value)
    {
        await using var app = Program.CreateWebApplication([$"--Database={value}"]);
        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        context.Database.ProviderName.Should().NotContain("Npgsql");
        context.Database.ProviderName.Should().Contain("MySql");
    }

    [Theory]
    [InlineData("")]
    [InlineData("sqlite")]
    [InlineData("postgresql")]
    public void EmptyOrUnknownDatabase_FailsDuringApplicationCreation(string value)
    {
        var action = () => Program.CreateWebApplication([$"--Database={value}"]);

        var exception = action.Should().Throw<InvalidOperationException>().Which;

        exception.Message.Should().Contain("Database");
        exception.Message.Should().Contain(string.IsNullOrEmpty(value) ? "''" : value);
        exception.Message.Should().NotContain("SOCODB_DB_PASSWORD");
    }

    [Theory]
    [InlineData("8.0.0")]
    [InlineData("8.0.36")]
    [InlineData("8.4.3")]
    [InlineData("9.1.0")]
    public void SupportedMySqlVersion_IsAccepted(string value)
    {
        DatabaseStartup.IsSupportedMySqlVersion(value, out var parsed).Should().BeTrue();
        parsed.Should().NotBeNull();
    }

    [Theory]
    [InlineData("5.7.44")]
    [InlineData("7.9.0")]
    [InlineData("8.0.0-MariaDB")]
    [InlineData("")]
    public void UnsupportedMySqlVersion_IsRejected(string value)
    {
        DatabaseStartup.IsSupportedMySqlVersion(value, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("openiddict_cache_entries")]
    [InlineData("cache_01")]
    public void MySqlCacheSettings_AcceptsSafeTableNames(string tableName)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=localhost;Database=openid;User ID=app;Password=secret;",
                ["MySqlCache:TableName"] = tableName
            })
            .Build();

        var settings = MySqlCacheSettings.FromConfiguration(configuration);

        settings.TableName.Should().Be(tableName);
        settings.SchemaName.Should().Be("openid");
    }

    [Fact]
    public void MySqlCacheSettings_EnablesUserVariablesForPomeloQueries()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=localhost;Database=openid;User ID=app;Password=secret;"
            })
            .Build();

        var settings = MySqlCacheSettings.FromConfiguration(configuration);

        new MySqlConnectionStringBuilder(settings.ConnectionString)
            .AllowUserVariables.Should().BeTrue();
    }

    [Theory]
    [InlineData("cache-name")]
    [InlineData("cache.name")]
    [InlineData("cache`name")]
    public void MySqlCacheSettings_RejectsUnsafeTableNames(string tableName)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=openid;",
                ["MySqlCache:TableName"] = tableName
            })
            .Build();

        var action = () => MySqlCacheSettings.FromConfiguration(configuration);

        action.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("MySqlCache:TableName");
    }

    [Fact]
    public void MigrationLists_AreIsolatedByContext()
    {
        var identityOptions = Microsoft.Extensions.Options.Options.Create(new IdentityExtensionOptions());
        var postgresOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=openid;",
                options => options
                    .MigrationsHistoryTable("openiddict_migrations_history")
                    .MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name))
            .Options;
        var mySqlOptions = new DbContextOptionsBuilder<MySqlAppDbContext>()
            .UseMySql(
                "Server=localhost;Database=openid;",
                new MySqlServerVersion(new Version(8, 0, 0)),
                options => options
                    .MigrationsHistoryTable("openiddict_migrations_history")
                    .MigrationsAssembly(typeof(MySqlAppDbContext).Assembly.GetName().Name))
            .Options;

        using var postgres = new AppDbContext(postgresOptions, identityOptions);
        using var mySql = new MySqlAppDbContext(mySqlOptions, identityOptions);

        postgres.Database.GetMigrations().Should().Equal("20260527135000_OpenIddictSchema");
        mySql.Database.GetMigrations().Should().Equal("20260907121156_MySqlOpenIddictSchema");
    }

    [Fact]
    public void DesignTimeFactories_AreExactForEachContext()
    {
        var factoryInterface = typeof(Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<>);
        var factories = typeof(AppDbContext).Assembly.GetTypes()
            .Where(type => !type.IsAbstract)
            .SelectMany(type => type.GetInterfaces()
                .Where(interfaceType => interfaceType.IsGenericType &&
                    interfaceType.GetGenericTypeDefinition() == factoryInterface)
                .Select(interfaceType => new
                {
                    Factory = type,
                    Context = interfaceType.GetGenericArguments()[0]
                }))
            .ToArray();

        factories.Should().ContainSingle(item => item.Context == typeof(AppDbContext));
        factories.Should().ContainSingle(item => item.Context == typeof(MySqlAppDbContext));
    }

    [Fact]
    public void DesignTimeConfiguration_ResolvesEnvironmentPlaceholders()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        path.Should().NotBeNullOrEmpty();

        var connectionString = DatabaseDesignTimeConfiguration.SubstituteEnvironmentVariables(
            "Server=localhost;Password=${PATH};Database=openid;");

        connectionString.Should().Be($"Server=localhost;Password={path};Database=openid;");
    }

    [Fact]
    public async Task MySqlConfiguration_RegistersDatabaseBackedCacheOnly()
    {
        await using var app = Program.CreateWebApplication(["--Database=mysql"]);

        var caches = app.Services.GetServices<IDistributedCache>().ToArray();

        caches.Should().ContainSingle();
        caches[0].GetType().FullName.Should().Be("Pomelo.Extensions.Caching.MySql.MySqlCache");
    }
}
