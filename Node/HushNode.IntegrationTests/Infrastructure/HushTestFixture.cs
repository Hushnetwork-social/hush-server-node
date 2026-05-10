using Microsoft.EntityFrameworkCore;
using Npgsql;
using StackExchange.Redis;
using System.Text.Json;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;
using HushServerNode;
using HushServerNode.Testing;
using HushShared.Elections.Model;
using HushShared.Elections.PublicationProof;

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
    private string? _protocolPackageCatalogRoot;
    private string? _protocolPackageCatalogPath;

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
        InitializeSp07RustWorkerPath();

        // Start containers in parallel for faster initialization
        var postgresTask = StartPostgresContainerAsync();
        var redisTask = StartRedisContainerAsync();

        await Task.WhenAll(postgresTask, redisTask);

        // Establish Redis connection for FLUSHDB operations (requires allowAdmin)
        var redisOptions = ConfigurationOptions.Parse(_redisContainer!.GetConnectionString());
        redisOptions.AllowAdmin = true;
        _redisConnection = await ConnectionMultiplexer.ConnectAsync(redisOptions);

        WriteApprovedProtocolPackageCatalog();
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

        if (_protocolPackageCatalogRoot is not null &&
            Directory.Exists(_protocolPackageCatalogRoot))
        {
            Directory.Delete(_protocolPackageCatalogRoot, recursive: true);
        }
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
    /// Uses dynamic ports for test isolation.
    /// </summary>
    /// <param name="diagnosticCapture">Optional diagnostic capture for collecting logs.</param>
    /// <returns>A running HushServerNodeCore instance</returns>
    public async Task<(HushServerNodeCore Node, BlockProductionControl BlockControl, GrpcClientFactory GrpcFactory)> StartNodeAsync(
        DiagnosticCapture? diagnosticCapture = null,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null)
    {
        var blockControl = new BlockProductionControl();
        var node = HushServerNodeCore.CreateForTesting(
            blockControl,
            PostgresConnectionString,
            RedisConnectionString,  // FEAT-046: Pass Redis connection for cache testing
            diagnosticCapture,
            MergeDefaultConfigurationOverrides(configurationOverrides));

        await node.StartAsync();

        var grpcFactory = new GrpcClientFactory("localhost", node.GrpcPort);

        return (node, blockControl, grpcFactory);
    }

    /// <summary>
    /// Creates and starts a new HushServerNodeCore configured for E2E testing.
    /// Uses fixed ports (14665/14666) so pre-built Docker images can connect.
    /// </summary>
    /// <param name="diagnosticCapture">Optional diagnostic capture for collecting logs.</param>
    /// <returns>A running HushServerNodeCore instance</returns>
    public async Task<(HushServerNodeCore Node, BlockProductionControl BlockControl, GrpcClientFactory GrpcFactory)> StartNodeForE2EAsync(
        DiagnosticCapture? diagnosticCapture = null,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null)
    {
        var blockControl = new BlockProductionControl();
        var node = HushServerNodeCore.CreateForE2ETesting(
            blockControl,
            PostgresConnectionString,
            RedisConnectionString,
            diagnosticCapture,
            MergeDefaultConfigurationOverrides(configurationOverrides));

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

        // Wait for the database to be truly ready (not just TCP connection)
        await WaitForDatabaseReadyAsync();
    }

    /// <summary>
    /// Waits for the PostgreSQL database to be fully initialized and accepting connections.
    /// Testcontainers may report the container as ready before the database exists.
    /// </summary>
    private async Task WaitForDatabaseReadyAsync()
    {
        var maxAttempts = 30;
        var delayMs = 500;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(PostgresConnectionString);
                await connection.OpenAsync();

                // Verify we can run a simple query
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                await command.ExecuteScalarAsync();

                return; // Database is ready
            }
            catch (NpgsqlException) when (attempt < maxAttempts)
            {
                // Database not ready yet, wait and retry
                await Task.Delay(delayMs);
            }
        }

        throw new InvalidOperationException(
            $"PostgreSQL database 'hush_test' not ready after {maxAttempts * delayMs / 1000} seconds");
    }

    private async Task StartRedisContainerAsync()
    {
        _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .WithCleanUp(true)
            .Build();

        await _redisContainer.StartAsync();
    }

    private IReadOnlyDictionary<string, string?> MergeDefaultConfigurationOverrides(
        IReadOnlyDictionary<string, string?>? configurationOverrides)
    {
        var merged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (configurationOverrides is not null)
        {
            foreach (var pair in configurationOverrides)
            {
                merged[pair.Key] = pair.Value;
            }
        }

        if (!merged.ContainsKey("Elections:ProtocolPackages:ApprovedCatalogRelativePath"))
        {
            merged["Elections:ProtocolPackages:ApprovedCatalogRelativePath"] = _protocolPackageCatalogPath
                ?? throw new InvalidOperationException("Test protocol package catalog has not been initialized.");
        }

        return merged;
    }

    private void WriteApprovedProtocolPackageCatalog()
    {
        _protocolPackageCatalogRoot = Path.Combine(
            Path.GetTempPath(),
            $"hush-integration-protocol-package-{Guid.NewGuid():N}");
        _protocolPackageCatalogPath = Path.Combine(
            _protocolPackageCatalogRoot,
            "ApprovedProtocolPackageCatalog.json");

        Directory.CreateDirectory(_protocolPackageCatalogRoot);

        var catalogEntry = ElectionModelFactory.CreateApprovedProtocolPackageCatalogEntry(
            packageId: "omega-hushvoting-v1",
            packageVersion: "v1.2.0",
            specPackageHash: Hash('a'),
            proofPackageHash: Hash('b'),
            releaseManifestHash: Hash('c'),
            compatibleProfileIds:
            [
                "admin-dev-1of1",
                "admin-prod-1of1",
                "dkg-dev-3of5",
                "dkg-prod-3of5",
            ],
            approvalStatus: ProtocolPackageApprovalStatus.DraftPrivate,
            isLatestForCompatibleProfiles: true,
            specAccessLocations:
            [
                ElectionModelFactory.CreateProtocolPackageAccessLocation(
                    ProtocolPackageAccessLocationKind.PublicWebsite,
                    "Integration test protocol specification package",
                    "https://tests.hushnetwork.local/protocol-omega/hushvoting-v1/v1.2.0/spec.zip",
                    Hash('d')),
            ],
            proofAccessLocations:
            [
                ElectionModelFactory.CreateProtocolPackageAccessLocation(
                    ProtocolPackageAccessLocationKind.PublicWebsite,
                    "Integration test protocol proof package",
                    "https://tests.hushnetwork.local/protocol-omega/hushvoting-v1/v1.2.0/proof.zip",
                    Hash('e')),
            ],
            externalReviewStatus: ProtocolPackageExternalReviewStatus.NotReviewed,
            approvedAt: new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc));

        File.WriteAllText(
            _protocolPackageCatalogPath,
            JsonSerializer.Serialize(
                new[] { catalogEntry },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true,
                }));
    }

    private static void InitializeSp07RustWorkerPath()
    {
        var configured = Environment.GetEnvironmentVariable(
            Sp07RustWorkerProcessOptions.WorkerPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        var repositoryRoot = FindRepositoryRoot();
        var executableName = OperatingSystem.IsWindows()
            ? "hush-sp07-rust-worker.exe"
            : "hush-sp07-rust-worker";
        var candidates = new[]
        {
            Path.Combine(repositoryRoot, "Tools", "HushSp07RustWorker", "target", "release", executableName),
            Path.Combine(repositoryRoot, "Tools", "HushSp07RustWorker", "target", "debug", executableName),
        };

        var workerPath = candidates.FirstOrDefault(File.Exists);
        if (workerPath is null)
        {
            throw new InvalidOperationException(
                "Integration tests require a built SP-07 Rust worker. " +
                "Run cargo build in Tools/HushSp07RustWorker or set HUSH_SP07_RUST_WORKER_PATH.");
        }

        Environment.SetEnvironmentVariable(
            Sp07RustWorkerProcessOptions.WorkerPathEnvironmentVariable,
            workerPath);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Tools", "HushSp07RustWorker")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate hush-server-node repository root.");
    }

    private static string Hash(char seed) => new(seed, 64);
}
