using System.Reactive.Subjects;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Olimpo;
using HushNode.Interfaces;

namespace HushServerNode;

public class gRPCServerBootstraper : IBootstrapper
{
    private readonly IEnumerable<IGrpcDefinition> _grpcDefinitions;
    private readonly IConfiguration _configuration;
    private readonly ILogger<gRPCServerBootstraper> _logger;

    public Subject<string> BootstrapFinished { get; }

    public int Priority { get; set; } = 10;


    public gRPCServerBootstraper(
        IEnumerable<IGrpcDefinition> grpcDefinitions,
        IConfiguration configuration,
        ILogger<gRPCServerBootstraper> logger)
    {
        this._grpcDefinitions = grpcDefinitions;
        this._configuration = configuration;
        this._logger = logger;

        this.BootstrapFinished = new Subject<string>();
    }

    public void Shutdown()
    {
    }

    public Task Startup()
    {
        var port = _configuration.GetValue<int>("ServerInfo:ListeningPort", 4665);

        _logger.LogInformation("Starting gRPC server on port {Port}...", port);

        var rcpServer = new Grpc.Core.Server
        {
            Ports = { new ServerPort("0.0.0.0", port, ServerCredentials.Insecure) }
        };

        foreach (var grpcDefinition in this._grpcDefinitions)
        {
            grpcDefinition.AddGrpcService(rcpServer);
        }

        rcpServer.Start();

        _logger.LogInformation("gRPC server started successfully on port {Port}", port);

        this.BootstrapFinished.OnNext("gRPC");
        return Task.CompletedTask;
    }
}
