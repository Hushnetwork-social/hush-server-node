using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Olimpo;
using HushNode.Blockchain;
using HushNode.Blockchain.gRPC;
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

namespace HushServerNode;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure configuration sources
        ConfigureConfigurationBuilder(builder.Configuration);

        // Configure Kestrel with separate endpoints for native gRPC (HTTP/2) and gRPC-Web (HTTP/1.1)
        // Without TLS, HTTP/2 requires "prior knowledge" mode - separate ports are needed
        var nativeGrpcPort = builder.Configuration.GetValue<int>("ServerInfo:ListeningPort", 4665);
        var grpcWebPort = builder.Configuration.GetValue<int>("ServerInfo:GrpcWebPort", 4666);
        builder.WebHost.ConfigureKestrel(options =>
        {
            // HTTP/2 only for native gRPC clients (Desktop, Android, iOS)
            options.ListenAnyIP(nativeGrpcPort, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });

            // HTTP/1.1 only for gRPC-Web clients (Browser)
            options.ListenAnyIP(grpcWebPort, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http1;
            });
        });

        // Add gRPC services
        builder.Services.AddGrpc();
        builder.Services.AddGrpcReflection();

        // Add CORS for browser clients
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

        // Register application services
        builder.Services.AddSingleton<IBlockchainCache, BlockchainCache>();

        builder.Services.AddDbContext<HushNodeDbContext>((provider, options) =>
        {
            options.UseNpgsql(builder.Configuration.GetConnectionString("HushNetworkDb"));
            options.EnableSensitiveDataLogging();  // For debugging
            options.EnableDetailedErrors();  // For debugging
        });

        builder.Services.AddHostedService<Worker>();

        // Register module services using the Host property for IHostBuilder extensions
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
            .RegisterPushNotificationsModule();

        var app = builder.Build();

        // Apply database migrations
        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
            dbContext.Database.Migrate();
        }

        // Enable CORS
        app.UseCors();

        // Enable gRPC-Web middleware (must be after UseCors and before MapGrpcService)
        app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

        // Map gRPC services
        app.MapGrpcService<BlockchainGrpcService>();
        app.MapGrpcService<BankGrpService>();
        app.MapGrpcService<IdentityGrpcService>();
        app.MapGrpcService<FeedsGrpcService>();
        app.MapGrpcService<ReactionsGrpcService>();
        app.MapGrpcService<MembershipGrpcService>();
        app.MapGrpcService<NotificationGrpcService>();

        // Enable gRPC reflection for grpcurl and other testing tools
        app.MapGrpcReflectionService();

        app.Run();
    }

    public static IConfigurationBuilder ConfigureConfigurationBuilder(IConfigurationBuilder configurationBuilder)
    {
        configurationBuilder ??= new ConfigurationBuilder();

        // Get the directory where the executable is located
        var basePath = AppContext.BaseDirectory;

        configurationBuilder
            .SetBasePath(basePath)
            .AddJsonFile("ApplicationSettings.json")
            .AddEnvironmentVariables();

        return configurationBuilder;
    }
}

