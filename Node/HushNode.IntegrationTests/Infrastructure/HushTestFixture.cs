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

        // Establish Redis connection for FLUSHDB operations (requires allowAdmin)
        var redisOptions = ConfigurationOptions.Parse(_redisContainer!.GetConnectionString());
        redisOptions.AllowAdmin = true;
        _redisConnection = await ConnectionMultiplexer.ConnectAsync(redisOptions);
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
    /// Resets the database by dropping and recreating the public schema.
    /// This ensures complete isolation between test scenarios.
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();

        // Terminate any other connections to allow dropping schema
        await using var terminateCommand = connection.CreateCommand();
        terminateCommand.CommandText = """
            SELECT pg_terminate_backend(pid)
            FROM pg_stat_activity
            WHERE datname = current_database()
            AND pid <> pg_backend_pid();
            """;
        await terminateCommand.ExecuteNonQueryAsync();

        // Drop and recreate the public schema - cleanest way to reset
        await using var resetCommand = connection.CreateCommand();
        resetCommand.CommandText = """
            DROP SCHEMA public CASCADE;
            CREATE SCHEMA public;
            GRANT ALL ON SCHEMA public TO public;
            """;
        await resetCommand.ExecuteNonQueryAsync();
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
    /// <param name="diagnosticCapture">Optional diagnostic capture for collecting logs.</param>
    /// <returns>A running HushServerNodeCore instance</returns>
    public async Task<(HushServerNodeCore Node, BlockProductionControl BlockControl, GrpcClientFactory GrpcFactory)> StartNodeAsync(
        DiagnosticCapture? diagnosticCapture = null)
    {
        var blockControl = new BlockProductionControl();
        var node = HushServerNodeCore.CreateForTesting(
            blockControl,
            PostgresConnectionString,
            RedisConnectionString,  // FEAT-046: Pass Redis connection for cache testing
            diagnosticCapture);

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

    /// <summary>
    /// Executes a scalar SQL query against the PostgreSQL database.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="sql">The SQL query to execute.</param>
    /// <returns>The result of the query, or default if null.</returns>
    public async Task<T?> ExecuteScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var result = await command.ExecuteScalarAsync();
        return result is DBNull ? default : (T?)result;
    }

    /// <summary>
    /// Executes a SQL query and returns all rows as a list of dynamic objects.
    /// </summary>
    /// <param name="sql">The SQL query to execute.</param>
    /// <returns>List of rows as dictionaries.</returns>
    public async Task<List<Dictionary<string, object?>>> ExecuteQueryAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var results = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            results.Add(row);
        }

        return results;
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
