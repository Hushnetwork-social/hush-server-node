using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Grpc.Net.Client;
using HushNetwork.proto;
using HushServerNode;
using HushServerNode.Testing;
using Microsoft.Playwright;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace HushVoting.IntegrationTests.Infrastructure;

/// <summary>Owns a real node, frontend process and isolated browser storage for one scenario.</summary>
internal sealed class HushVotingScenario : IAsyncDisposable
{
    private Process? _frontend;
    private Task? _stdoutDrain;
    private Task? _stderrDrain;
    private GrpcChannel? _channel;
    private string? _credentialSourceDirectory;
    private string? _temporaryRoot;
    private IBrowserType? _persistentBrowserType;
    private string? _browserProfileDirectory;
    public HushServerNodeCore Node { get; private set; } = null!;
    public HushVotingNodeProcess? NodeProcess { get; private set; }
    public BlockProductionControl Blocks { get; private set; } = null!;
    public IBrowserContext Context { get; private set; } = null!;
    public IPage Page { get; private set; } = null!;
    public HushIdentity.HushIdentityClient Identities { get; private set; } = null!;
    public HushBlockchain.HushBlockchainClient Blockchain { get; private set; } = null!;
    public HushNetwork.proto.HushVotingLicence.HushVotingLicenceClient Licences { get; private set; } = null!;
    public string BaseUrl { get; private set; } = "";
    private string _redis = "";
    private HushVotingTestRun? _run;
    public const string DevicePassword = "HushVoting-test-device-password-42";
    public bool CaptureEnabled => false;
    public HushVotingFaultInterceptor Faults { get; } = new();
    public HushVotingBlockClock? HistoricalBlockClock { get; set; }

    public async Task<string> CreateEncryptedCredentialSourceAsync(byte[] encrypted)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The HushVoting runner requires Linux.");
        _credentialSourceDirectory ??= Path.Combine(_temporaryRoot ?? throw new InvalidOperationException("Scenario not started."), "source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_credentialSourceDirectory);
        File.SetUnixFileMode(_credentialSourceDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var path = Path.Combine(_credentialSourceDirectory, Guid.NewGuid().ToString("N") + ".dat");
        await File.WriteAllBytesAsync(path, encrypted);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return path;
    }

    public async Task StartAsync(HushVotingTestRun run, bool includeBrowser = true, bool useNodeProcess = false)
    {
        _run = run;
        if (includeBrowser && useNodeProcess) throw new InvalidOperationException("Node process Twins require backend-only setup.");
        _temporaryRoot = Path.GetDirectoryName(run.ProtocolCatalog);
        await run.ResetStorageAsync();
        _redis = run.Redis;

        if (useNodeProcess)
        {
            NodeProcess = new HushVotingNodeProcess(run.Postgres, run.Redis, run.ProtocolCatalog);
            await NodeProcess.StartAsync();
            return;
        }

        Blocks = new BlockProductionControl();
        Node = HushServerNodeCore.CreateForTesting(Blocks, run.Postgres, run.Redis,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Elections:ProtocolPackages:ApprovedCatalogRelativePath"] = run.ProtocolCatalog,
                ["Logging:LogLevel:Default"] = "None",
                ["Elections:DeploymentProof:LocalDevelopmentProfileIds"] = "admin-dev-1of1;admin-prod-1of1;dkg-dev-3of5;dkg-prod-3of5"
            }, configureTestServices: services =>
            {
                services.AddSingleton(Faults);
                if (HistoricalBlockClock is not null) services.AddSingleton<TimeProvider>(HistoricalBlockClock);
                services.AddSingleton<HushNode.MemPool.MemPoolService>();
                services.AddSingleton<HushNode.MemPool.IMemPoolService>(provider =>
                    new HushVotingDeferredMemPool(provider.GetRequiredService<HushNode.MemPool.MemPoolService>(), Faults));
                services.AddGrpc(options => options.Interceptors.Add<HushVotingFaultInterceptor>());
            });
        await Node.StartAsync();
        _channel = GrpcChannel.ForAddress($"http://localhost:{Node.GrpcPort}");
        Blockchain = new HushBlockchain.HushBlockchainClient(_channel);
        Identities = new HushIdentity.HushIdentityClient(_channel);
        Licences = new HushNetwork.proto.HushVotingLicence.HushVotingLicenceClient(_channel);

        // Backend Twins share only the owned server/database lifecycle.
        if (!includeBrowser) return;

        var clientRoot = Environment.GetEnvironmentVariable("HUSHVOTING_E2E_CLIENT_ROOT")
            ?? throw new InvalidOperationException("HushVoting client root is missing; use the dedicated runner.");
        var standalone = Path.Combine(clientRoot, ".next-web", "standalone");
        if (!File.Exists(Path.Combine(standalone, "server.js")))
            throw new FileNotFoundException("Build the HushVoting production frontend with the dedicated runner's --build option.");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        BaseUrl = $"http://localhost:{port}";
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = standalone, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        HushVotingCorpusInputs.RemoveFromChildEnvironment(start.Environment);
        start.ArgumentList.Add("server.js");
        start.Environment["PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["HOSTNAME"] = "127.0.0.1";
        start.Environment["HUSHSERVER_NODE_ENDPOINT"] = $"localhost:{Node.GrpcPort}";
        start.Environment["NODE_ENV"] = "production";
        _frontend = Process.Start(start) ?? throw new InvalidOperationException("Cannot start HushVoting frontend");
        // Never persist raw frontend output: these scenarios handle credentials.
        _stdoutDrain = _frontend.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        _stderrDrain = _frontend.StandardError.BaseStream.CopyToAsync(Stream.Null);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        using var readiness = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            if (_frontend.HasExited) throw new InvalidOperationException($"HushVoting frontend exited with code {_frontend.ExitCode}");
            try
            {
                using var response = await http.GetAsync(BaseUrl, readiness.Token);
                if (response.IsSuccessStatusCode) break;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!readiness.IsCancellationRequested) { }
            await Task.Delay(100, readiness.Token);
        }
        Context = await run.Browser.NewContextAsync(new() { BaseURL = BaseUrl, ViewportSize = new() { Width = 1280, Height = 800 } });
        Context.SetDefaultTimeout(15_000);
        // No trace, video, screenshot, console, request-body or storage capture.
        Page = await Context.NewPageAsync();
    }

    // A fresh incognito context for each corpus member; no source/password or
    // staged vault can carry into the next member. The node remains run-owned.
    internal async Task PrepareCorpusBrowserAsync()
    {
        if (_run is null || Node is null || _frontend is null || _frontend.HasExited
            || _browserProfileDirectory is not null || CaptureEnabled
            || !ReferenceEquals(Node.Services.GetRequiredService<HushVotingFaultInterceptor>(), Faults)
            || _frontend.StartInfo.Environment["HUSHSERVER_NODE_ENDPOINT"] != $"localhost:{Node.GrpcPort}")
            throw new InvalidOperationException("Corpus browser requires the owned uncaptured Web/node fixture.");
        _run.RequireOwnedLocalCorpusNetwork();
        await Blockchain.GetBlockchainHeightAsync(new(), deadline: DateTime.UtcNow.AddSeconds(10));
        await Context.CloseAsync();
        Context = await _run.Browser.NewContextAsync(new()
        {
            BaseURL = BaseUrl, ViewportSize = new() { Width = 1280, Height = 800 },
            ServiceWorkers = ServiceWorkerPolicy.Block, AcceptDownloads = false
        });
        Context.SetDefaultTimeout(15_000);
        Page = await Context.NewPageAsync();
    }

    public async Task ClearCacheAsync()
    {
        var options = ConfigurationOptions.Parse(_redis);
        options.AllowAdmin = true;
        using var redis = await ConnectionMultiplexer.ConnectAsync(options);
        await redis.GetServer(redis.GetEndPoints()[0]).FlushDatabaseAsync();
    }

    public Task WithRedisStoppedAsync(Func<Task> action) =>
        (_run ?? throw new InvalidOperationException("Scenario is not started.")).WithRedisStoppedAsync(action);

    public Task WithRedisWritesRejectedAsync(Func<Task> action) =>
        (_run ?? throw new InvalidOperationException("Scenario is not started.")).WithRedisWritesRejectedAsync(action);

    public async Task ResetStoppedNodeChainAsync()
    {
        if (NodeProcess is null || !NodeProcess.HasExited || Page is not null || Node is not null)
            throw new InvalidOperationException("Chain reset requires the stopped backend-only owned node process.");
        await (_run ?? throw new InvalidOperationException("Scenario is not started.")).ResetStorageAsync();
        await NodeProcess.RestartAsync();
    }

    public async Task UseRestartableBrowserAsync()
    {
        _persistentBrowserType = Context.Browser!.BrowserType;
        await Context.CloseAsync();
        _browserProfileDirectory = Path.Combine(_temporaryRoot ?? throw new InvalidOperationException("Scenario not started."), "browser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_browserProfileDirectory);
        await LaunchPersistentBrowserAsync();
    }

    private async Task LaunchPersistentBrowserAsync()
    {
        Context = await _persistentBrowserType!.LaunchPersistentContextAsync(_browserProfileDirectory!, new()
        {
            Headless = true, BaseURL = BaseUrl, ViewportSize = new() { Width = 1280, Height = 800 },
            Args = ["--disable-breakpad", "--disable-crash-reporter", "--hide-crash-restore-bubble"]
        });
        Context.SetDefaultTimeout(15_000);
        Page = Context.Pages.FirstOrDefault() ?? await Context.NewPageAsync();

    }

    public async Task CrashAndRestartBrowserAsync()
    {
        if (_browserProfileDirectory is null) throw new InvalidOperationException("Scenario does not own a restartable browser.");
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Context.Close += (_, _) => closed.TrySetResult();
        var session = await Context.NewCDPSessionAsync(Page);
        try { await session.SendAsync("Browser.crash"); }
        catch (PlaywrightException) { /* The command loses its transport when the browser dies. */ }
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await LaunchPersistentBrowserAsync();
        await Page.GotoAsync("/");
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        async Task Attempt(Func<Task> action)
        {
            try { await action(); } catch (Exception error) { failures.Add(error); }
        }
        if (Context is not null) await Attempt(() => Context.CloseAsync());
        if (_frontend is not null)
        {
            await Attempt(async () =>
            {
                if (!_frontend.HasExited) _frontend.Kill(entireProcessTree: true);
                await _frontend.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                await Task.WhenAll(_stdoutDrain ?? Task.CompletedTask, _stderrDrain ?? Task.CompletedTask);
                _frontend.Dispose();
            });
        }
        _channel?.Dispose();
        if (NodeProcess is not null) await Attempt(() => NodeProcess.DisposeAsync().AsTask());
        if (Node is not null) await Attempt(() => Node.DisposeAsync().AsTask());
        if (_credentialSourceDirectory is not null) await Attempt(() =>
        {
            Directory.Delete(_credentialSourceDirectory, recursive: true);
            return Task.CompletedTask;
        });
        if (_browserProfileDirectory is not null) await Attempt(() =>
        {
            Directory.Delete(_browserProfileDirectory, recursive: true);
            return Task.CompletedTask;
        });
        Blocks?.Dispose();
        if (failures.Count > 0) throw new AggregateException("HushVoting scenario cleanup failed", failures);
    }
}
