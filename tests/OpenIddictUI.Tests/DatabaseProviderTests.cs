using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Moq;
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
    [InlineData("8.0", 8, 0, 0)]
    [InlineData("8.0.0", 8, 0, 0)]
    [InlineData("8.0.36", 8, 0, 36)]
    [InlineData("8.4", 8, 4, 0)]
    [InlineData("8.4.3", 8, 4, 3)]
    [InlineData("9.1.0", 9, 1, 0)]
    public void SupportedMySqlVersion_IsAccepted(string value, int major, int minor, int patch)
    {
        DatabaseStartup.IsSupportedMySqlVersion(value, out var parsed).Should().BeTrue();
        parsed.Should().Be(new Version(major, minor, patch));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("5.7.44")]
    [InlineData("7.9.0")]
    [InlineData("8.0.0-MariaDB")]
    [InlineData("not-a-version")]
    [InlineData("8.x.1")]
    [InlineData("8")]
    [InlineData("")]
    public void UnsupportedMySqlVersion_IsRejected(string? value)
    {
        DatabaseStartup.IsSupportedMySqlVersion(value, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-version")]
    [InlineData("8.x.1")]
    [InlineData("8")]
    [InlineData("")]
    public void MalformedMySqlVersion_DoesNotProduceParsedVersion(string? value)
    {
        DatabaseStartup.IsSupportedMySqlVersion(value, out var parsed).Should().BeFalse();
        parsed.Should().BeNull();
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

    [Theory]
    [InlineData("")]
    [InlineData("schema-name")]
    [InlineData("schema.name")]
    [InlineData("schema`name")]
    public void MySqlCacheSettings_RejectsUnsafeSchemaNames(string schemaName)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=openid;",
                ["MySqlCache:SchemaName"] = schemaName
            })
            .Build();

        var action = () => MySqlCacheSettings.FromConfiguration(configuration);

        action.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("MySqlCache:SchemaName");
    }

    [Fact]
    public void MySqlCacheSettings_RejectsUnsafeDatabaseNameUsedAsSchema()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=openid-db;"
            })
            .Build();

        var action = () => MySqlCacheSettings.FromConfiguration(configuration);

        action.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("DefaultConnection database");
    }

    [Theory]
    [InlineData("MySqlCache:ExpiredItemsDeletionInterval", "not-a-duration")]
    [InlineData("MySqlCache:ExpiredItemsDeletionInterval", "00:00:00")]
    [InlineData("MySqlCache:ExpiredItemsDeletionInterval", "-00:01:00")]
    [InlineData("MySqlCache:DefaultSlidingExpiration", "not-a-duration")]
    [InlineData("MySqlCache:DefaultSlidingExpiration", "00:00:00")]
    [InlineData("MySqlCache:DefaultSlidingExpiration", "-00:01:00")]
    public void MySqlCacheSettings_RejectsInvalidDurations(string key, string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=openid;",
                [key] = value
            })
            .Build();

        var action = () => MySqlCacheSettings.FromConfiguration(configuration);

        action.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain(key);
    }

    [Fact]
    public void MySqlCacheSettings_RejectsMissingConnectionStringWithoutLeakingPassword()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var action = () => MySqlCacheSettings.FromConfiguration(configuration);

        var exception = action.Should().Throw<InvalidOperationException>().Which;
        exception.ToString().Should().NotContain("secret");
        exception.Message.Should().Contain("ConnectionStrings:DefaultConnection");
    }

    [Fact]
    public void MySqlCacheSettings_RejectsInvalidConnectionStringWithoutLeakingPassword()
    {
        var password = "mysql-startup-secret";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    $"Server=localhost;Database=openid;Password={password};Unknown Option=1;"
            })
            .Build();

        var action = () => MySqlCacheSettings.FromConfiguration(configuration);

        var exception = action.Should().Throw<InvalidOperationException>().Which;
        exception.ToString().Should().NotContain(password);
        exception.Message.Should().Contain("ConnectionStrings:DefaultConnection");
    }

    [Fact]
    public void MySqlCacheSettings_UsesExpectedDefaults()
    {
        var settings = CreateMySqlCacheSettings();

        settings.ExpiredItemsDeletionInterval.Should().BeNull();
        settings.DefaultSlidingExpiration.Should().Be(TimeSpan.FromMinutes(20));
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
    public void DesignTimeFactories_CreateProviderAndMigrationBoundContexts()
    {
        using var postgres = new AppDbContextFactory().CreateDbContext([]);
        using var mySql = new MySqlAppDbContextFactory().CreateDbContext([]);

        postgres.Database.ProviderName.Should().Contain("Npgsql");
        mySql.Database.ProviderName.Should().Contain("MySql");
        postgres.Database.GetMigrations().Should().Equal("20260527135000_OpenIddictSchema");
        mySql.Database.GetMigrations().Should().Equal("20260907121156_MySqlOpenIddictSchema");
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

    [Fact]
    public async Task PostgreSqlConfiguration_PreservesConfiguredCacheSettings()
    {
        await using var app = Program.CreateWebApplication(
        [
            "--Database=postgre",
            "--PostgresCache:SchemaName=legacy",
            "--PostgresCache:TableName=legacy_cache",
            "--PostgresCache:CreateIfNotExists=false",
            "--PostgresCache:UseWAL=true",
            "--PostgresCache:ExpiredItemsDeletionInterval=00:07:00",
            "--PostgresCache:DefaultSlidingExpiration=00:03:00"
        ]);

        var options = app.Services.GetRequiredService<IOptions<PostgresCacheOptions>>().Value;
        var caches = app.Services.GetServices<IDistributedCache>().ToArray();

        options.SchemaName.Should().Be("legacy");
        options.TableName.Should().Be("legacy_cache");
        options.CreateIfNotExists.Should().BeFalse();
        options.UseWAL.Should().BeTrue();
        options.ExpiredItemsDeletionInterval.Should().Be(TimeSpan.FromMinutes(7));
        options.DefaultSlidingExpiration.Should().Be(TimeSpan.FromMinutes(3));
        caches.Should().ContainSingle();
        caches[0].GetType().FullName.Should().Be("Microsoft.Extensions.Caching.Postgres.PostgresCache");
    }

    [Fact]
    public async Task PostgreSqlInitialization_DoesNotResolveCacheBeforeMigration()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();

        await DatabaseStartup.InitializeAsync(services, DatabaseProvider.Postgres);
    }

    [Fact]
    public async Task PostgreSqlInitialization_DoesNotProbeUnavailableCacheBeforeMigration()
    {
        var cache = new Mock<IDistributedCache>(MockBehavior.Strict);
        cache.Setup(item => item.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cache table is unavailable"));
        await using var services = new ServiceCollection()
            .AddSingleton<IOptions<PostgresCacheOptions>>(
                Microsoft.Extensions.Options.Options.Create(new PostgresCacheOptions { CreateIfNotExists = false }))
            .AddSingleton<IDistributedCache>(cache.Object)
            .BuildServiceProvider();

        await DatabaseStartup.InitializeAsync(services, DatabaseProvider.Postgres);

        cache.Verify(
            item => item.GetAsync("__openiddictui_startup_probe__", It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MySqlInitialization_PreparesCacheBeforeProbe()
    {
        var connection = new StubMySqlStartupConnection();
        var cache = new Mock<IDistributedCache>();
        cache.Setup(item => item.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        await DatabaseStartup.InitializeMySqlAsync(
            CreateMySqlCacheSettings(),
            cache.Object,
            connection);

        connection.Calls.Should().Equal("open", "version", "ensure-cache-table");
        cache.Verify(
            item => item.GetAsync("__openiddictui_startup_probe__", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-version")]
    [InlineData("5.7.44")]
    [InlineData("8.0.0-MariaDB")]
    public async Task MySqlInitialization_RejectsUnsupportedVersionBeforeCachePreparation(
        string? serverVersion)
    {
        var connection = new StubMySqlStartupConnection { ServerVersion = serverVersion };
        var cache = new Mock<IDistributedCache>(MockBehavior.Strict);

        var action = () => DatabaseStartup.InitializeMySqlAsync(
            CreateMySqlCacheSettings(),
            cache.Object,
            connection);

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();

        exception.Which.Message.Should().Contain("unsupported");
        connection.Calls.Should().Equal("open", "version");
        cache.Verify(
            item => item.GetAsync("__openiddictui_startup_probe__", It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MySqlInitialization_PropagatesConnectionFailureWithoutProbingCache()
    {
        var connection = new StubMySqlStartupConnection
        {
            OpenException = new InvalidOperationException("connection failed")
        };
        var cache = new Mock<IDistributedCache>(MockBehavior.Strict);

        var action = () => DatabaseStartup.InitializeMySqlAsync(
            CreateMySqlCacheSettings(),
            cache.Object,
            connection);

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();

        exception.Which.Message.Should().Be("connection failed");
        connection.Calls.Should().Equal("open");
        cache.Verify(
            item => item.GetAsync("__openiddictui_startup_probe__", It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MySqlInitialization_PropagatesCacheTablePreparationFailure()
    {
        var connection = new StubMySqlStartupConnection
        {
            EnsureCacheTableException = new InvalidOperationException("cache table preparation failed")
        };
        var cache = new Mock<IDistributedCache>(MockBehavior.Strict);

        var action = () => DatabaseStartup.InitializeMySqlAsync(
            CreateMySqlCacheSettings(),
            cache.Object,
            connection);

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();

        exception.Which.Message.Should().Be("cache table preparation failed");
        connection.Calls.Should().Equal("open", "version", "ensure-cache-table");
        cache.Verify(
            item => item.GetAsync("__openiddictui_startup_probe__", It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MySqlInitialization_PropagatesCacheProbeFailure()
    {
        var connection = new StubMySqlStartupConnection();
        var cache = new Mock<IDistributedCache>(MockBehavior.Strict);
        cache.Setup(item => item.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cache probe failed"));

        var action = () => DatabaseStartup.InitializeMySqlAsync(
            CreateMySqlCacheSettings(),
            cache.Object,
            connection);

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();

        exception.Which.Message.Should().Be("cache probe failed");
        connection.Calls.Should().Equal("open", "version", "ensure-cache-table");
    }

    [Fact]
    public async Task MySqlInitialization_ConnectionFailureDoesNotExposePassword()
    {
        const string password = "mysql-startup-secret";
        var settings = CreateMySqlCacheSettings(
            $"Server=127.0.0.1;Port=1;Database=openid;User ID=app;Password={password};Connection Timeout=1;");
        var cache = new Mock<IDistributedCache>(MockBehavior.Strict);
        await using var services = new ServiceCollection()
            .AddSingleton(settings)
            .AddSingleton<IDistributedCache>(cache.Object)
            .BuildServiceProvider();

        var action = () => DatabaseStartup.InitializeAsync(services, DatabaseProvider.MySql);

        var exception = await action.Should().ThrowAsync<Exception>();

        exception.Which.ToString().Should().NotContain(password);
        cache.Verify(
            item => item.GetAsync("__openiddictui_startup_probe__", It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void MySqlCacheSchemaValidation_AcceptsExpectedColumnsAndIndexes()
    {
        DatabaseStartup.ValidateCacheColumns(CreateValidCacheColumns());
        DatabaseStartup.ValidateCacheIndexes(CreateValidCacheIndexes());
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("Value")]
    [InlineData("ExpiresAtTime")]
    [InlineData("SlidingExpirationInSeconds")]
    [InlineData("AbsoluteExpiration")]
    public void MySqlCacheSchemaValidation_RejectsMissingRequiredColumn(string columnName)
    {
        var columns = CreateValidCacheColumns();
        columns.Remove(columnName);

        var action = () => DatabaseStartup.ValidateCacheColumns(columns);

        action.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain(columnName);
    }

    [Fact]
    public void MySqlCacheSchemaValidation_RejectsIncompatibleColumn()
    {
        var columns = CreateValidCacheColumns();
        columns["Id"] = new("varchar", "NO", 449, null, "utf8mb4_bin");

        var action = () => DatabaseStartup.ValidateCacheColumns(columns);

        action.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Id");
    }

    [Fact]
    public void MySqlCacheSchemaValidation_RejectsMissingPrimaryKey()
    {
        var indexes = CreateValidCacheIndexes();
        indexes.RemoveAt(0);

        var action = () => DatabaseStartup.ValidateCacheIndexes(indexes);

        action.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("primary key");
    }

    [Fact]
    public void MySqlCacheSchemaValidation_RejectsMissingExpirationIndex()
    {
        var indexes = CreateValidCacheIndexes();
        indexes.RemoveAt(1);

        var action = () => DatabaseStartup.ValidateCacheIndexes(indexes);

        action.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("ExpiresAtTime");
    }

    private static MySqlCacheSettings CreateMySqlCacheSettings(string? connectionString = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString ??
                    "Server=localhost;Database=openid;User ID=app;Password=secret;"
            })
            .Build();

        return MySqlCacheSettings.FromConfiguration(configuration);
    }

    private static Dictionary<string, DatabaseStartup.CacheColumnDefinition> CreateValidCacheColumns()
    {
        return new Dictionary<string, DatabaseStartup.CacheColumnDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Id"] = new("varchar", "NO", 449, null, "ascii_bin"),
            ["Value"] = new("longblob", "NO", null, null, null),
            ["ExpiresAtTime"] = new("datetime", "NO", null, 6, null),
            ["SlidingExpirationInSeconds"] = new("bigint", "YES", null, null, null),
            ["AbsoluteExpiration"] = new("datetime", "YES", null, 6, null)
        };
    }

    private static List<DatabaseStartup.CacheIndexDefinition> CreateValidCacheIndexes()
    {
        return
        [
            new("PRIMARY", "Id", 1),
            new("ix_expires_at_time", "ExpiresAtTime", 1)
        ];
    }

    private sealed class StubMySqlStartupConnection : IMySqlStartupConnection
    {
        public List<string> Calls { get; } = [];

        public string? ServerVersion { get; set; } = "8.0.36";

        public Exception? OpenException { get; set; }

        public Exception? EnsureCacheTableException { get; set; }

        public Task OpenAsync(CancellationToken cancellationToken)
        {
            Calls.Add("open");
            return OpenException is null
                ? Task.CompletedTask
                : Task.FromException(OpenException);
        }

        public Task<string?> ReadServerVersionAsync(CancellationToken cancellationToken)
        {
            Calls.Add("version");
            return Task.FromResult(ServerVersion);
        }

        public Task EnsureCacheTableAsync(
            MySqlCacheSettings settings,
            CancellationToken cancellationToken)
        {
            Calls.Add("ensure-cache-table");
            return EnsureCacheTableException is null
                ? Task.CompletedTask
                : Task.FromException(EnsureCacheTableException);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
