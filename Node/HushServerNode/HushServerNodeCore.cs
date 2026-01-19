using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Olimpo;
using HushNode.Blockchain;
using HushNode.Blockchain.Configuration;
using HushNode.Blockchain.gRPC;
using HushNode.Blockchain.Services;
using HushNode.Blockchain.Storage;
using HushNode.Blockchain.Workflows;
using HushNode.Credentials;
using HushNode.Indexing;
using HushNode.Identity;
using HushNode.Identity.gRPC;
using HushNode.MemPool;
using HushNode.Bank;
using HushNode.Bank.gRPC;
using HushNode.Feeds;
using HushNode.Feeds.gRPC;
using HushNode.Reactions;
using HushNode.Reactions.gRPC;
using HushNode.Caching;
using HushNode.Notifications.gRPC;
using HushNode.PushNotifications;
using HushNode.UrlMetadata.gRPC;
using HushServerNode.Testing;

namespace HushServerNode;

/// <summary>
/// Core orchestrator for HushServerNode that encapsulates all node startup logic.
/// This class is internal and accessible to integration tests via InternalsVisibleTo.
/// </summary>
internal sealed class HushServerNodeCore : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly BlockProductionControl? _blockProductionControl;
    private readonly bool _isTestMode;
    private Task? _runTask;

    /// <summary>
    /// Gets the actual gRPC port that Kestrel is listening on.
    /// For test mode, this is the dynamically assigned port.
    /// </summary>
    public int GrpcPort { get; private set; }

    /// <summary>
    /// Gets the gRPC-Web port for browser clients.
    /// </summary>
    public int GrpcWebPort { get; private set; }

    private HushServerNodeCore(WebApplication app, BlockProductionControl? blockProductionControl, bool isTestMode)
    {
        _app = app;
        _blockProductionControl = blockProductionControl;
        _isTestMode = isTestMode;
    }

    /// <summary>
    /// Creates a HushServerNodeCore configured for production use.
    /// Uses the standard configuration from ApplicationSettings.json.
    /// </summary>
    public static HushServerNodeCore CreateForProduction(string[] args)
    {
        var app = BuildApplication(args, testConfig: null);
        return new HushServerNodeCore(app, blockProductionControl: null, isTestMode: false);
    }

    /// <summary>
    /// Creates a HushServerNodeCore configured for integration testing.
    /// Uses dynamic ports and injected block production control.
    /// </summary>
    /// <param name="blockProductionControl">The block production control for synchronous block triggers.</param>
    /// <param name="connectionString">PostgreSQL connection string for test database.</param>
    public static HushServerNodeCore CreateForTesting(
        BlockProductionControl blockProductionControl,
        string connectionString)
    {
        var testConfig = new TestConfiguration(blockProductionControl, connectionString);
        var app = BuildApplication(Array.Empty<string>(), testConfig);
        return new HushServerNodeCore(app, blockProductionControl, isTestMode: true);
    }

    /// <summary>
    /// Runs the node and blocks until shutdown is requested.
    /// Used for production mode where the node runs until terminated.
    /// </summary>
    public async Task RunAsync()
    {
        await _app.RunAsync();
    }

    /// <summary>
    /// Starts the node and returns immediately.
    /// The node continues running until DisposeAsync is called.
    /// Used for test mode where the caller controls the node lifecycle.
    /// </summary>
    public async Task StartAsync()
    {
        // Start the application
        _runTask = _app.RunAsync();

        // Wait briefly for Kestrel to start and bind ports
        await Task.Delay(100);

        // Extract actual bound ports from Kestrel
        var addresses = _app.Urls;
        foreach (var address in addresses)
        {
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
            {
                // The first port is native gRPC (HTTP/2), second is gRPC-Web (HTTP/1.1)
                if (GrpcPort == 0)
                {
                    GrpcPort = uri.Port;
                }
                else
                {
                    GrpcWebPort = uri.Port;
                }
            }
        }
    }

    /// <summary>
    /// Triggers block production and waits for the block to be finalized.
    /// Only available in test mode.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if not in test mode.</exception>
    public async Task ProduceBlockAsync()
    {
        if (!_isTestMode || _blockProductionControl == null)
        {
            throw new InvalidOperationException("ProduceBlockAsync is only available in test mode.");
        }

        await _blockProductionControl.ProduceBlockAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();

        if (_runTask != null)
        {
            try
            {
                await _runTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }

        _blockProductionControl?.Dispose();
        await _app.DisposeAsync();
    }

    private static WebApplication BuildApplication(string[] args, TestConfiguration? testConfig)
    {
        var builder = WebApplication.CreateBuilder(args);

        if (testConfig == null)
        {
            // Production mode: use standard configuration
            ConfigureConfigurationBuilder(builder.Configuration);

            var nativeGrpcPort = builder.Configuration.GetValue<int>("ServerInfo:ListeningPort", 4665);
            var grpcWebPort = builder.Configuration.GetValue<int>("ServerInfo:GrpcWebPort", 4666);

            ConfigureKestrel(builder.WebHost, nativeGrpcPort, grpcWebPort);
        }
        else
        {
            // Test mode: use in-memory configuration with dynamic ports
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:HushNetworkDb"] = testConfig.ConnectionString,
                ["BlockchainSettings:MaxEmptyBlocksBeforePause"] = "100" // High value for tests
            });

            // Use port 0 for dynamic port allocation
            ConfigureKestrel(builder.WebHost, nativeGrpcPort: 0, grpcWebPort: 0);
        }

        ConfigureServices(builder, testConfig);

        var app = builder.Build();

        // Apply database migrations
        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
            dbContext.Database.Migrate();
        }

        ConfigureMiddleware(app);

        return app;
    }

    private static void ConfigureKestrel(IWebHostBuilder webHost, int nativeGrpcPort, int grpcWebPort)
    {
        webHost.ConfigureKestrel(options =>
        {
            // HTTP/2 only for native gRPC clients
            options.ListenAnyIP(nativeGrpcPort, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });

            // HTTP/1.1 only for gRPC-Web clients
            options.ListenAnyIP(grpcWebPort, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http1;
            });
        });
    }

    private static void ConfigureServices(WebApplicationBuilder builder, TestConfiguration? testConfig)
    {
        builder.Services.AddGrpc();
        builder.Services.AddGrpcReflection();

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding");
            });
        });

        builder.Services.AddSingleton<IBlockchainCache, BlockchainCache>();

        builder.Services.AddDbContext<HushNodeDbContext>((provider, options) =>
        {
            var connectionString = testConfig?.ConnectionString
                ?? builder.Configuration.GetConnectionString("HushNetworkDb");
            options.UseNpgsql(connectionString);
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
        });

        builder.Services.AddHostedService<Worker>();

        // Register module services
        builder.Host
            .UseSystemd()
            .RegisterBootstrapperManager()
            .RegisterEventAggregatorManager()
            .RegisterHushCredentials()
            .RegisterCoreModuleBlockchain()
            .RegisterCoreModuleBank()
            .RegisterCoreModuleFeeds()
            .RegisterCoreModuleReactions()
            .RegisterHushNodeIndexing()
            .RegisterHushNodeMemPool()
            .RegisterInternalModuleIdentity()
            .RegisterNotificationGrpc()
            .RegisterPushNotificationsModule()
            .RegisterCoreModuleUrlMetadata();

        // In test mode, replace the BlockProductionSchedulerService with one using injected observable
        if (testConfig != null)
        {
            var (observableFactory, onBlockFinalized) = testConfig.BlockProductionControl.GetSchedulerConfiguration();

            // Remove the default registration and add our custom one
            builder.Services.AddSingleton<IBlockProductionSchedulerService>(sp =>
            {
                return new BlockProductionSchedulerService(
                    sp.GetRequiredService<IBlockAssemblerWorkflow>(),
                    sp.GetRequiredService<IMemPoolService>(),
                    sp.GetRequiredService<IBlockchainStorageService>(),
                    sp.GetRequiredService<IBlockchainCache>(),
                    sp.GetRequiredService<IEventAggregator>(),
                    sp.GetRequiredService<IOptions<BlockchainSettings>>(),
                    sp.GetRequiredService<ILogger<BlockProductionSchedulerService>>(),
                    observableFactory,
                    onBlockFinalized);
            });
        }
    }

    private static void ConfigureMiddleware(WebApplication app)
    {
        app.UseCors();
        app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

        app.MapGrpcService<BlockchainGrpcService>();
        app.MapGrpcService<BankGrpService>();
        app.MapGrpcService<IdentityGrpcService>();
        app.MapGrpcService<FeedsGrpcService>();
        app.MapGrpcService<ReactionsGrpcService>();
        app.MapGrpcService<MembershipGrpcService>();
        app.MapGrpcService<NotificationGrpcService>();
        app.MapGrpcService<UrlMetadataGrpcService>();

        app.MapGrpcReflectionService();
    }

    private static IConfigurationBuilder ConfigureConfigurationBuilder(IConfigurationBuilder configurationBuilder)
    {
        // Get the directory where the executable is located
        var basePath = AppContext.BaseDirectory;

        configurationBuilder
            .SetBasePath(basePath)
            .AddJsonFile("ApplicationSettings.json")
            .AddEnvironmentVariables();

        return configurationBuilder;
    }

    private sealed record TestConfiguration(
        BlockProductionControl BlockProductionControl,
        string ConnectionString);
}
