using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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

    [Fact]
    public void UnsupportedMySqlVersion_WithUnrepresentablePatch_IsRejected()
    {
        DatabaseStartup.IsSupportedMySqlVersion("7.9.99999999999999999999", out _).Should().BeFalse();
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
    public void MigrationAssembliesAndScripts_AreIsolatedByProvider()
    {
        var identityOptions = Microsoft.Extensions.Options.Options.Create(new IdentityExtensionOptions());
        var postgresOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=openid;",
                options => options.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name))
            .Options;
        var mySqlOptions = new DbContextOptionsBuilder<MySqlAppDbContext>()
            .UseMySql(
                "Server=localhost;Database=openid;",
                new MySqlServerVersion(new Version(8, 0, 0)),
                options => options.MigrationsAssembly(typeof(MySqlAppDbContext).Assembly.GetName().Name))
            .Options;

        using var postgres = new AppDbContext(postgresOptions, identityOptions);
        using var mySql = new MySqlAppDbContext(mySqlOptions, identityOptions);

        ReferenceEquals(postgres.GetService<IMigrationsAssembly>().Assembly, typeof(AppDbContext).Assembly).Should().BeTrue();
        ReferenceEquals(mySql.GetService<IMigrationsAssembly>().Assembly, typeof(MySqlAppDbContext).Assembly).Should().BeTrue();
        var postgresScript = postgres.Database.GenerateCreateScript();
        var mySqlScript = mySql.Database.GenerateCreateScript();

        postgresScript.Should().Contain("CREATE TABLE");
        postgresScript.Should().Contain("openiddict_applications");
        mySqlScript.Should().Contain("CREATE TABLE");
        mySqlScript.Should().Contain("openiddict_applications");
        postgresScript.Should().NotBe(mySqlScript);
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
        var events = new List<string>();
        var connection = new StubMySqlStartupConnection(events);
        var cache = new Mock<IDistributedCache>();
        cache.Setup(item => item.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => events.Add("probe"))
            .ReturnsAsync((byte[]?)null);

        await DatabaseStartup.InitializeMySqlAsync(
            CreateMySqlCacheSettings(),
            cache.Object,
            connection);

        events.Should().Equal("open", "version", "ensure-cache-table", "probe");
    }

    [Fact]
    public async Task MySqlStartupConnection_ForwardsSqlOperationsAndUsesExpectedCommands()
    {
        var connection = new RecordingDbConnection(
            new RecordingCommandPlan { ScalarResult = "8.0.36" },
            new RecordingCommandPlan(),
            new RecordingCommandPlan { Rows = CreateValidCacheColumnRows() },
            new RecordingCommandPlan { Rows = CreateValidCacheIndexRows() });
        await using var startupConnection = new DatabaseStartup.MySqlStartupConnection(connection);

        var version = await startupConnection.ReadServerVersionAsync(CancellationToken.None);
        await startupConnection.EnsureCacheTableAsync(CreateMySqlCacheSettings(), CancellationToken.None);

        version.Should().Be("8.0.36");
        connection.Commands.Should().HaveCount(4);
        connection.Commands[0].CommandText.Should().Be("SELECT VERSION();");
        connection.Commands[1].CommandText.Should().Contain("CREATE TABLE IF NOT EXISTS `openid`.`openiddict_cache_entries`");
        connection.Commands[1].CommandText.Should().Contain("`Id` varchar(449) CHARACTER SET ascii COLLATE ascii_bin NOT NULL");
        connection.Commands[1].CommandText.Should().Contain("PRIMARY KEY (`Id`)");
        connection.Commands[1].CommandText.Should().Contain("KEY `ix_expires_at_time` (`ExpiresAtTime`)");
        connection.Commands[2].CommandText.Should().Contain("FROM information_schema.COLUMNS");
        connection.Commands[2].Parameters.Cast<DbParameter>().Should().Contain(parameter =>
            parameter.ParameterName == "@schema" && Equals(parameter.Value, "openid"));
        connection.Commands[2].Parameters.Cast<DbParameter>().Should().Contain(parameter =>
            parameter.ParameterName == "@table" && Equals(parameter.Value, "openiddict_cache_entries"));
        connection.Commands[3].CommandText.Should().Contain("FROM information_schema.STATISTICS");
    }

    [Fact]
    public void MySqlStartupConnection_RejectsNullConnection()
    {
        var action = () => new DatabaseStartup.MySqlStartupConnection((DbConnection)null!);

        action.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("connection");
    }

    [Fact]
    public async Task ReadServerVersion_PropagatesCommandFailure()
    {
        var expected = new InvalidOperationException("version query failed");
        var connection = new RecordingDbConnection(new RecordingCommandPlan { Exception = expected });

        Func<Task> action = async () => await DatabaseStartup.ReadServerVersionAsync(connection, CancellationToken.None);

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task ReadServerVersion_ConvertsNullScalarToEmptyString()
    {
        var connection = new RecordingDbConnection(new RecordingCommandPlan { ScalarResult = null });

        var version = await DatabaseStartup.ReadServerVersionAsync(connection, CancellationToken.None);

        version.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureCacheTable_PropagatesCreateFailure()
    {
        var expected = new InvalidOperationException("cache table create failed");
        var connection = new RecordingDbConnection(new RecordingCommandPlan { Exception = expected });

        var action = () => DatabaseStartup.EnsureCacheTableAsync(
            connection,
            CreateMySqlCacheSettings(),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(expected);
        connection.Commands.Should().HaveCount(1);
    }

    [Fact]
    public async Task EnsureCacheTable_StopsAtColumnQueryFailure()
    {
        var expected = new InvalidOperationException("column query failed");
        var connection = new RecordingDbConnection(
            new RecordingCommandPlan(),
            new RecordingCommandPlan { Exception = expected });

        var action = () => DatabaseStartup.EnsureCacheTableAsync(
            connection,
            CreateMySqlCacheSettings(),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(expected);
        connection.Commands.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("cache", "`cache`")]
    [InlineData("cache`name", "`cache``name`")]
    public void QuoteIdentifier_EscapesMySqlIdentifier(string identifier, string expected)
    {
        DatabaseStartup.QuoteIdentifier(identifier).Should().Be(expected);
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
    [MemberData(nameof(IncompatibleCacheColumns))]
    public void MySqlCacheSchemaValidation_RejectsEveryIncompatibleColumnShape(
        string columnName,
        object incompatibleColumn)
    {
        var columns = CreateValidCacheColumns();
        columns[columnName] = (DatabaseStartup.CacheColumnDefinition)incompatibleColumn;

        var action = () => DatabaseStartup.ValidateCacheColumns(columns);

        action.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain(columnName);
    }

    public static IEnumerable<object[]> IncompatibleCacheColumns()
    {
        yield return new object[] { "Id", new DatabaseStartup.CacheColumnDefinition("text", "NO", 449, null, "ascii_bin") };
        yield return new object[] { "Id", new DatabaseStartup.CacheColumnDefinition("varchar", "YES", 449, null, "ascii_bin") };
        yield return new object[] { "Id", new DatabaseStartup.CacheColumnDefinition("varchar", "NO", 448, null, "ascii_bin") };
        yield return new object[] { "Id", new DatabaseStartup.CacheColumnDefinition("varchar", "NO", 449, null, "utf8mb4_bin") };
        yield return new object[] { "Value", new DatabaseStartup.CacheColumnDefinition("blob", "NO", null, null, null) };
        yield return new object[] { "Value", new DatabaseStartup.CacheColumnDefinition("longblob", "YES", null, null, null) };
        yield return new object[] { "ExpiresAtTime", new DatabaseStartup.CacheColumnDefinition("timestamp", "NO", null, 6, null) };
        yield return new object[] { "ExpiresAtTime", new DatabaseStartup.CacheColumnDefinition("datetime", "YES", null, 6, null) };
        yield return new object[] { "ExpiresAtTime", new DatabaseStartup.CacheColumnDefinition("datetime", "NO", null, 3, null) };
        yield return new object[] { "SlidingExpirationInSeconds", new DatabaseStartup.CacheColumnDefinition("int", "YES", null, null, null) };
        yield return new object[] { "SlidingExpirationInSeconds", new DatabaseStartup.CacheColumnDefinition("bigint", "NO", null, null, null) };
        yield return new object[] { "AbsoluteExpiration", new DatabaseStartup.CacheColumnDefinition("timestamp", "YES", null, 6, null) };
        yield return new object[] { "AbsoluteExpiration", new DatabaseStartup.CacheColumnDefinition("datetime", "NO", null, 6, null) };
        yield return new object[] { "AbsoluteExpiration", new DatabaseStartup.CacheColumnDefinition("datetime", "YES", null, 3, null) };
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

    [Theory]
    [InlineData("pk_cache", "Id", 1)]
    [InlineData("PRIMARY", "Value", 1)]
    [InlineData("PRIMARY", "Id", 2)]
    public void MySqlCacheSchemaValidation_RejectsInvalidPrimaryKeyShape(
        string name,
        string column,
        int sequence)
    {
        var indexes = CreateValidCacheIndexes();
        indexes[0] = new(name, column, sequence);

        var action = () => DatabaseStartup.ValidateCacheIndexes(indexes, "openid.cache");

        action.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("openid.cache");
    }

    [Theory]
    [InlineData("Id", 1)]
    [InlineData("ExpiresAtTime", 2)]
    public void MySqlCacheSchemaValidation_RejectsInvalidExpirationIndexShape(string column, int sequence)
    {
        var indexes = CreateValidCacheIndexes();
        indexes[1] = new("ix_expires_at_time", column, sequence);

        var action = () => DatabaseStartup.ValidateCacheIndexes(indexes, "openid.cache");

        action.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("openid.cache");
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

    private static IReadOnlyList<object?[]> CreateValidCacheColumnRows() =>
    [
        ["Id", "varchar", "NO", 449L, null, "ascii_bin"],
        ["Value", "longblob", "NO", null, null, null],
        ["ExpiresAtTime", "datetime", "NO", null, 6, null],
        ["SlidingExpirationInSeconds", "bigint", "YES", null, null, null],
        ["AbsoluteExpiration", "datetime", "YES", null, 6, null]
    ];

    private static IReadOnlyList<object?[]> CreateValidCacheIndexRows() =>
    [
        ["PRIMARY", "Id", 1],
        ["ix_expires_at_time", "ExpiresAtTime", 1]
    ];

    private sealed class StubMySqlStartupConnection : IMySqlStartupConnection
    {
        public StubMySqlStartupConnection()
            : this([])
        {
        }

        public StubMySqlStartupConnection(List<string> calls)
        {
            Calls = calls;
        }

        public List<string> Calls { get; }

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

    private sealed class RecordingCommandPlan
    {
        public object? ScalarResult { get; init; }

        public IReadOnlyList<object?[]> Rows { get; init; } = [];

        public Exception? Exception { get; init; }
    }

    private sealed class RecordingDbConnection : DbConnection
    {
        private readonly Queue<RecordingCommandPlan> _plans;
        private ConnectionState _state = ConnectionState.Closed;

        public RecordingDbConnection(params RecordingCommandPlan[] plans)
        {
            _plans = new Queue<RecordingCommandPlan>(plans);
        }

        public List<RecordingDbCommand> Commands { get; } = [];

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "openid";

        public override string DataSource => "recording";

        public override string ServerVersion => "8.0.36";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
            _state = ConnectionState.Closed;
        }

        public override void Open()
        {
            _state = ConnectionState.Open;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand()
        {
            if (!_plans.TryDequeue(out var plan))
            {
                throw new InvalidOperationException("No command plan was configured.");
            }

            var command = new RecordingDbCommand(plan)
            {
                Connection = this
            };
            Commands.Add(command);
            return command;
        }
    }

    private sealed class RecordingDbCommand : DbCommand
    {
        private readonly RecordingCommandPlan _plan;
        private readonly RecordingDbParameterCollection _parameters = new();

        public RecordingDbCommand(RecordingCommandPlan plan)
        {
            _plan = plan;
        }

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; } = CommandType.Text;

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; }

        protected override DbParameterCollection DbParameterCollection => _parameters;

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
        {
            ThrowIfConfigured();
            return 0;
        }

        public override object? ExecuteScalar()
        {
            ThrowIfConfigured();
            return _plan.ScalarResult;
        }

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new RecordingDbParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            ThrowIfConfigured();

            var table = new DataTable();
            var columnCount = _plan.Rows.Count == 0 ? 0 : _plan.Rows.Max(row => row.Length);
            for (var index = 0; index < columnCount; index++)
            {
                table.Columns.Add($"Column{index}", typeof(object));
            }

            foreach (var row in _plan.Rows)
            {
                var values = Enumerable.Repeat<object?>(null, columnCount)
                    .Select((value, index) => index < row.Length ? row[index] ?? DBNull.Value : DBNull.Value)
                    .ToArray();
                table.Rows.Add(values);
            }

            return table.CreateDataReader();
        }

        private void ThrowIfConfigured()
        {
            if (_plan.Exception is not null)
            {
                throw _plan.Exception;
            }
        }
    }

    private sealed class RecordingDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _items = [];

        public override int Count => _items.Count;

        protected override DbParameter GetParameter(int index) => _items[index];

        protected override DbParameter GetParameter(string parameterName) =>
            _items.First(parameter => parameter.ParameterName == parameterName);

        public override int Add(object value)
        {
            var parameter = value as DbParameter ?? throw new ArgumentException("Expected a database parameter.", nameof(value));
            _items.Add(parameter);
            return _items.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                Add(value!);
            }
        }

        public override void Clear() => _items.Clear();

        public override bool Contains(object? value) => value is DbParameter parameter && _items.Contains(parameter);

        public override bool Contains(string? value) => _items.Any(parameter => parameter.ParameterName == value);

        public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

        public override IEnumerator GetEnumerator() => _items.GetEnumerator();

        public override int IndexOf(object? value) => value is DbParameter parameter ? _items.IndexOf(parameter) : -1;

        public override int IndexOf(string? parameterName) =>
            _items.FindIndex(parameter => parameter.ParameterName == parameterName);

        public override void Insert(int index, object value)
        {
            var parameter = value as DbParameter ?? throw new ArgumentException("Expected a database parameter.", nameof(value));
            _items.Insert(index, parameter);
        }

        public override void Remove(object? value)
        {
            if (value is DbParameter parameter)
            {
                _items.Remove(parameter);
            }
        }

        public override void RemoveAt(int index) => _items.RemoveAt(index);

        public override void RemoveAt(string? parameterName) => RemoveAt(IndexOf(parameterName));

        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;

        protected override void SetParameter(string? parameterName, DbParameter value) =>
            _items[IndexOf(parameterName)] = value;

        public override object SyncRoot => this;
    }

    private sealed class RecordingDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

        public override bool IsNullable { get; set; }

        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;

        public override byte Precision { get; set; }

        public override byte Scale { get; set; }

        public override int Size { get; set; }

        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;

        public override bool SourceColumnNullMapping { get; set; }

        public override object? Value { get; set; }

        public override void ResetDbType()
        {
        }
    }
}
