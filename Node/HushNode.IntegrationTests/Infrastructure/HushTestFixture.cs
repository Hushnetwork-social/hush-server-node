using Microsoft.EntityFrameworkCore;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;
using HushServerNode;
using HushServerNode.Testing;

namespace HushNode.IntegrationTests.Infrastructure;

/// <summary>
/// Test fixture that manages TestContainers lifecycle for PostgreSQL and Redis,
/// and provides HushServerNodeCore instances for integration testing.
/// Containers are shared across scenarios for performance, but data is reset between scenarios.
/// </summary>
internal sealed class HushTestFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgresContainer;
    private RedisContainer? _redisContainer;
    private ConnectionMultiplexer? _redisConnection;

    /// <summary>
    /// Gets the PostgreSQL connection string for the test container.
    /// </summary>
    public string PostgresConnectionString => _postgresContainer?.GetConnectionString()
        ?? throw new InvalidOperationException("PostgreSQL container not started");

    /// <summary>
    /// Gets the Redis connection string for the test container.
    /// </summary>
    public string RedisConnectionString => _redisContainer?.GetConnectionString()
        ?? throw new InvalidOperationException("Redis container not started");

    /// <summary>
    /// Gets the Redis connection for direct operations.
    /// </summary>
    public ConnectionMultiplexer RedisConnection => _redisConnection
        ?? throw new InvalidOperationException("Redis connection not established");

    /// <summary>
    /// Starts the PostgreSQL and Redis containers.
    /// Called once at the beginning of the test run.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Start containers in parallel for faster initialization
        var postgresTask = StartPostgresContainerAsync();
        var redisTask = StartRedisContainerAsync();

        await Task.WhenAll(postgresTask, redisTask);

        // Establish Redis connection for FLUSHDB operations
        _redisConnection = await ConnectionMultiplexer.ConnectAsync(_redisContainer!.GetConnectionString());
    }

    /// <summary>
    /// Stops all containers and cleans up resources.
    /// Called once at the end of the test run.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_redisConnection != null)
        {
            await _redisConnection.CloseAsync();
            _redisConnection.Dispose();
        }

        var disposeTasks = new List<Task>();

        if (_postgresContainer != null)
        {
            disposeTasks.Add(_postgresContainer.DisposeAsync().AsTask());
        }

        if (_redisContainer != null)
        {
            disposeTasks.Add(_redisContainer.DisposeAsync().AsTask());
        }

        await Task.WhenAll(disposeTasks);
    }

    /// <summary>
    /// Resets the database by dropping all tables and reapplying migrations.
    /// Call this before each scenario for test isolation.
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        // Use raw SQL to drop all tables, then let the node recreate schema via migrations
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();

        // Drop all tables in the public schema
        await using var dropCommand = connection.CreateCommand();
        dropCommand.CommandText = """
            DO $$
            DECLARE
                r RECORD;
            BEGIN
                -- Drop all tables
                FOR r IN (SELECT tablename FROM pg_tables WHERE schemaname = 'public') LOOP
                    EXECUTE 'DROP TABLE IF EXISTS "' || r.tablename || '" CASCADE';
                END LOOP;

                -- Drop EF migrations history if exists
                DROP TABLE IF EXISTS "__EFMigrationsHistory" CASCADE;
            END $$;
            """;
        await dropCommand.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Flushes all data from Redis.
    /// Call this before each scenario for test isolation.
    /// </summary>
    public async Task FlushRedisAsync()
    {
        var server = _redisConnection!.GetServer(_redisConnection.GetEndPoints()[0]);
        await server.FlushDatabaseAsync();
    }

    /// <summary>
    /// Creates and starts a new HushServerNodeCore configured for testing.
    /// </summary>
    /// <returns>A running HushServerNodeCore instance</returns>
    public async Task<(HushServerNodeCore Node, BlockProductionControl BlockControl, GrpcClientFactory GrpcFactory)> StartNodeAsync()
    {
        var blockControl = new BlockProductionControl();
        var node = HushServerNodeCore.CreateForTesting(blockControl, PostgresConnectionString);

        await node.StartAsync();

        var grpcFactory = new GrpcClientFactory("localhost", node.GrpcPort);

        return (node, blockControl, grpcFactory);
    }

    /// <summary>
    /// Resets both database and Redis for a clean scenario start.
    /// </summary>
    public async Task ResetAllAsync()
    {
        await Task.WhenAll(
            ResetDatabaseAsync(),
            FlushRedisAsync());
    }

    private async Task StartPostgresContainerAsync()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("hush_test")
            .WithUsername("hush_test")
            .WithPassword("hush_test")
            .WithCleanUp(true)
            .Build();

        await _postgresContainer.StartAsync();
    }

    private async Task StartRedisContainerAsync()
    {
        _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .WithCleanUp(true)
            .Build();

        await _redisContainer.StartAsync();
    }
}
