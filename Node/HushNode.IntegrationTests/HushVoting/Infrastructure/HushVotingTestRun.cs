using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using HushShared.Elections.Model;
using Microsoft.Playwright;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Npgsql;
using StackExchange.Redis;

namespace HushVoting.IntegrationTests.Infrastructure;

/// <summary>Run-owned infrastructure. No dependency on HushNetwork test code.</summary>
internal sealed class HushVotingTestRun : IAsyncDisposable
{
    private PostgreSqlContainer? _postgres;
    private RedisContainer? _redis;
    private IPlaywright? _playwright;
    private string? _temporaryDirectory;
    private string? _runId;
    public IBrowser Browser { get; private set; } = null!;
    public string Postgres => _postgres!.GetConnectionString();
    public string Redis => _redis!.GetConnectionString();
    public string ProtocolCatalog => Path.Combine(_temporaryDirectory!, "catalog.json");

    internal void RequireOwnedLocalCorpusNetwork()
    {
        if (_runId is null || _runId != Environment.GetEnvironmentVariable("HUSHVOTING_E2E_RUN_ID")
            || _postgres is null || _redis is null || Browser is null
            || !IsLoopback(new NpgsqlConnectionStringBuilder(Postgres).Host)
            || ConfigurationOptions.Parse(Redis).EndPoints.Any(endpoint => endpoint switch
            {
                DnsEndPoint dns => !IsLoopback(dns.Host),
                IPEndPoint ip => !IPAddress.IsLoopback(ip.Address),
                _ => true
            }))
            throw new InvalidOperationException("Controlled corpus requires this run's owned local infrastructure.");
    }

    private static bool IsLoopback(string? host) => host == "localhost"
        || IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);

    // Only the PG/Redis containers created by this run can be reset here.
    // Callers must stop the scenario's node before resetting an existing chain.
    public async Task ResetStorageAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var connection = new NpgsqlConnection(Postgres);
        await connection.OpenAsync(timeout.Token);
        await using var command = new NpgsqlCommand("""
            DO $$ DECLARE s record; BEGIN
              FOR s IN SELECT nspname FROM pg_namespace WHERE nspname NOT LIKE 'pg_%' AND nspname <> 'information_schema' LOOP
                EXECUTE format('DROP SCHEMA %I CASCADE', s.nspname);
              END LOOP;
            END $$;
            CREATE SCHEMA public;
            """, connection);
        await command.ExecuteNonQueryAsync(timeout.Token);
        var options = ConfigurationOptions.Parse(Redis);
        options.AllowAdmin = true;
        using var redis = await ConnectionMultiplexer.ConnectAsync(options).WaitAsync(timeout.Token);
        await redis.GetServer(redis.GetEndPoints()[0]).FlushDatabaseAsync().WaitAsync(timeout.Token);
    }

    // FEAT-011 Phase 3 Tasks 3.5–3.8: faults touch only this run's labelled Redis.
    public async Task WithRedisStoppedAsync(Func<Task> action)
    {
        var redis = _redis ?? throw new InvalidOperationException("Owned Redis is not started.");
        var originalPort = redis.GetMappedPublicPort(6379);
        try
        {
            using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await redis.StopAsync(stopping.Token);
            await action();
        }
        finally
        {
            using var restoring = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await redis.StartAsync(restoring.Token);
            if (redis.GetMappedPublicPort(6379) != originalPort)
                throw new InvalidOperationException("Owned Redis restart changed its test endpoint.");
        }
    }

    public async Task WithRedisWritesRejectedAsync(Func<Task> action)
    {
        var redis = _redis ?? throw new InvalidOperationException("Owned Redis is not started.");
        async Task Configure(string requiredReplicas)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await redis.ExecAsync(["redis-cli", "CONFIG", "SET", "min-replicas-to-write", requiredReplicas], timeout.Token);
            if (result.ExitCode != 0 || result.Stdout.Trim() != "OK")
                throw new InvalidOperationException("Owned Redis write-fault configuration failed; command output omitted.");
        }
        try { await Configure("1"); await action(); }
        finally { await Configure("0"); }
    }

    public async Task StartAsync(bool includeBrowser = true)
    {
        var runId = Environment.GetEnvironmentVariable("HUSHVOTING_E2E_RUN_ID")
            ?? throw new InvalidOperationException("Use scripts/run-hushvoting-e2e.sh so resource cleanup is supervised.");
        _runId = runId;
        _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"hushvoting-protocol-{runId}");
        Directory.CreateDirectory(_temporaryDirectory);
        var entry = ElectionModelFactory.CreateApprovedProtocolPackageCatalogEntry(
            packageId: "omega-hushvoting-v1", packageVersion: "v1.2.0",
            specPackageHash: new string('a', 64), proofPackageHash: new string('b', 64),
            releaseManifestHash: new string('c', 64),
            compatibleProfileIds: ["admin-dev-1of1", "admin-prod-1of1", "dkg-dev-3of5", "dkg-prod-3of5"],
            approvalStatus: ProtocolPackageApprovalStatus.DraftPrivate,
            isLatestForCompatibleProfiles: true,
            specAccessLocations: [ElectionModelFactory.CreateProtocolPackageAccessLocation(
                ProtocolPackageAccessLocationKind.PublicWebsite, "HushVoting test specification",
                "https://tests.hushvoting.invalid/spec.zip", new string('d', 64))],
            proofAccessLocations: [ElectionModelFactory.CreateProtocolPackageAccessLocation(
                ProtocolPackageAccessLocationKind.PublicWebsite, "HushVoting test proof package",
                "https://tests.hushvoting.invalid/proof.zip", new string('e', 64))],
            externalReviewStatus: ProtocolPackageExternalReviewStatus.NotReviewed,
            approvedAt: new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc));
        await File.WriteAllTextAsync(ProtocolCatalog, JsonSerializer.Serialize(new[] { entry },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine")
            .WithDatabase("hushvoting_e2e").WithUsername("hushvoting_test").WithPassword("hushvoting_test")
            .WithLabel("hushvoting.e2e.run", runId).Build();
        // Keep the endpoint stable through stop/start. Docker may reassign a
        // dynamically published port, which would test a configuration change
        // rather than recovery of the node's existing Redis connection.
        using var redisPort = new TcpListener(IPAddress.Loopback, 0);
        redisPort.Start();
        var retainedRedisPort = ((IPEndPoint)redisPort.LocalEndpoint).Port;
        redisPort.Stop();
        _redis = new RedisBuilder().WithImage("redis:7-alpine")
            .WithPortBinding(retainedRedisPort, 6379)
            .WithLabel("hushvoting.e2e.run", runId).Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await Task.WhenAll(_postgres.StartAsync(timeout.Token), _redis.StartAsync(timeout.Token));
        if (!includeBrowser) return;
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        async Task Attempt(Func<Task> action)
        {
            try { await action(); } catch (Exception error) { failures.Add(error); }
        }
        if (Browser is not null) await Attempt(() => Browser.CloseAsync());
        _playwright?.Dispose();
        if (_redis is not null) await Attempt(() => _redis.DisposeAsync().AsTask());
        if (_postgres is not null) await Attempt(() => _postgres.DisposeAsync().AsTask());
        if (_temporaryDirectory is not null && Directory.Exists(_temporaryDirectory))
            await Attempt(() => { Directory.Delete(_temporaryDirectory, true); return Task.CompletedTask; });
        if (failures.Count > 0) throw new AggregateException("HushVoting run cleanup failed", failures);
    }
}
