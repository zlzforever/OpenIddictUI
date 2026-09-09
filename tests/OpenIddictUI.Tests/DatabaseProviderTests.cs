using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

        ReferenceEquals(postgres.GetService<IMigrationsAssembly>().Assembly, typeof(AppDbContext).Assembly).Should()
            .BeTrue();
        ReferenceEquals(mySql.GetService<IMigrationsAssembly>().Assembly, typeof(MySqlAppDbContext).Assembly).Should()
            .BeTrue();
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

    private sealed class RecordingCommandPlan
    {
        public object? ScalarResult { get; init; }

        public IReadOnlyList<object?[]> Rows { get; init; } = [];

        public Exception? Exception { get; init; }

        public Func<string, Exception>? ExceptionFactory { get; init; }
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

        [AllowNull] public override string ConnectionString { get; set; } = string.Empty;

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

        [AllowNull] public override string CommandText { get; set; } = string.Empty;

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

            if (_plan.ExceptionFactory is not null)
            {
                throw _plan.ExceptionFactory(DbConnection?.ConnectionString ?? string.Empty);
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
            var parameter = value as DbParameter ??
                            throw new ArgumentException("Expected a database parameter.", nameof(value));
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
            var parameter = value as DbParameter ??
                            throw new ArgumentException("Expected a database parameter.", nameof(value));
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

        [AllowNull] public override string ParameterName { get; set; } = string.Empty;

        public override byte Precision { get; set; }

        public override byte Scale { get; set; }

        public override int Size { get; set; }

        [AllowNull] public override string SourceColumn { get; set; } = string.Empty;

        public override bool SourceColumnNullMapping { get; set; }

        public override object? Value { get; set; }

        public override void ResetDbType()
        {
        }
    }
}