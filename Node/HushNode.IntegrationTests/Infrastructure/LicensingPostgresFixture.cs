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
using Testcontainers.PostgreSql;
using Xunit;

namespace HushNode.IntegrationTests.Infrastructure;

/// <summary>
/// Shared real-PostgreSQL container for FEAT-013 licensing TwinTests. One container is
/// started per collection; tests own their databases so migration state is fully
/// controlled. Relational assertions never use EF InMemory or SQLite.
/// </summary>
public sealed class LicensingPostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("hush_test")
            .WithUsername("hush_test")
            .WithPassword("hush_test")
            .Build();

        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public string AdminConnectionString =>
        _container?.GetConnectionString() ?? throw new InvalidOperationException("container not started");

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

    /// <summary>Creates the unified HushNodeDbContext for a database on the shared container.</summary>
    public HushNodeDbContext CreateContext(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Database = databaseName,
            // FEAT-013 100-way concurrency tests run many simultaneous coordinator attempts. Keep the
            // client pool below the container's default Postgres max_connections so SQLSTATE 53300 is
            // never hit; logical concurrency is preserved (attempts queue on the pool).
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

    public async Task MigrateToAsync(string databaseName, string? targetMigration)
    {
        await using var context = CreateContext(databaseName);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(targetMigration);
    }

    private async Task<NpgsqlConnection> OpenAdminConnectionAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = "postgres" };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}

[CollectionDefinition("FEAT-013 Licensing PostgreSQL")]
public sealed class LicensingPostgresCollection : ICollectionFixture<LicensingPostgresFixture>
{
}
