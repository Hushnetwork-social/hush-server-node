using HushNode.Bank.Storage;
using HushNode.Blockchain.Storage;
using HushNode.Elections.Storage;
using HushNode.Feeds.Storage;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.Identity.Storage;
using HushNode.Interfaces;
using HushNode.PushNotifications;
using HushNode.Reactions.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace HushNode.IntegrationTests.Infrastructure;

/// <summary>
/// FEAT-014 Phase 7 shared real-Redis + real-PostgreSQL fixture. Tests create isolated databases
/// per scenario on the shared PostgreSQL container and use the shared Redis instance with a unique
/// per-test database index/prefix so scenario state never leaks. Redis key checks are asserted only
/// inside tests; no Redis key, subject digest, or envelope payload is ever logged.
/// </summary>
public sealed class LicenceCacheRedisPostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgresContainer;
    private RedisContainer? _redisContainer;
    private ConnectionMultiplexer? _redisConnection;
    private string? _instancePrefix;

    public async Task InitializeAsync()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("hush_test")
            .WithUsername("hush_test")
            .WithPassword("hush_test")
            .Build();
        await _postgresContainer.StartAsync();

        _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();
        await _redisContainer.StartAsync();

        var options = ConfigurationOptions.Parse(_redisContainer.GetConnectionString());
        options.AllowAdmin = true;
        _redisConnection = await ConnectionMultiplexer.ConnectAsync(options);
        _instancePrefix = "hushvoting-feat014-test:";
    }

    public async Task DisposeAsync()
    {
        if (_redisConnection is not null)
        {
            await _redisConnection.CloseAsync();
            _redisConnection.Dispose();
        }

        if (_redisContainer is not null)
        {
            await _redisContainer.DisposeAsync();
        }

        if (_postgresContainer is not null)
        {
            await _postgresContainer.DisposeAsync();
        }
    }

    public string PostgresAdminConnectionString =>
        _postgresContainer?.GetConnectionString() ?? throw new InvalidOperationException("Postgres not started");

    public string RedisConnectionString =>
        _redisContainer?.GetConnectionString() ?? throw new InvalidOperationException("Redis not started");

    public string InstancePrefix =>
        _instancePrefix ?? throw new InvalidOperationException("Redis prefix not initialized");

    public ConnectionMultiplexer RedisConnection =>
        _redisConnection ?? throw new InvalidOperationException("Redis not started");

    public IDatabase RedisDatabase =>
        _redisConnection?.GetDatabase() ?? throw new InvalidOperationException("Redis not started");

    /// <summary>Flushes the shared Redis database before each scenario that needs isolation.</summary>
    public Task FlushRedisAsync()
    {
        var server = _redisConnection!.GetServer(_redisConnection.GetEndPoints()[0]);
        return server.FlushDatabaseAsync();
    }

    public async Task CreateDatabaseAsync(string databaseName)
    {
        await using var connection = await OpenAdminConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await command.ExecuteNonQueryAsync();
    }

    public async Task DropDatabaseAsync(string databaseName)
    {
        await using var connection = await OpenAdminConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }

    public HushNodeDbContext CreateContext(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(PostgresAdminConnectionString)
        {
            Database = databaseName,
            MaxPoolSize = 24,
        };
        var options = new DbContextOptionsBuilder<HushNodeDbContext>()
            .UseNpgsql(builder.ConnectionString)
            .Options;

        IDbContextConfigurator[] configurators =
        {
            new BankDbContextConfigurator(),
            new BlockchainDbContextConfigurator(),
            new ElectionsDbContextConfigurator(),
            new FeedsDbContextConfigurator(),
            new IdentityDbContextConfigurator(),
            new PushNotificationsDbContextConfigurator(),
            new ReactionsDbContextConfigurator(),
            new LicensingDbContextConfigurator()
        };

        return new HushNodeDbContext(configurators, options);
    }

    public async Task MigrateToHeadAsync(string databaseName)
    {
        await using var context = CreateContext(databaseName);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync();
    }

    private async Task<NpgsqlConnection> OpenAdminConnectionAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(PostgresAdminConnectionString)
        {
            Database = "postgres",
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}

[CollectionDefinition("FEAT-014 Redis+PostgreSQL")]
public sealed class Feat014RedisPostgresCollection : ICollectionFixture<LicenceCacheRedisPostgresFixture>
{
}
