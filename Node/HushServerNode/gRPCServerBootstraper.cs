using System.Reactive.Subjects;
using Grpc.Core;
using Olimpo;
using HushNode.Interfaces;

namespace HushServerNode;

public class gRPCServerBootstraper : IBootstrapper
{
    private readonly IEnumerable<IGrpcDefinition> _grpcDefinitions;

    public Subject<string> BootstrapFinished { get; }

    public int Priority { get; set; } = 10;
        

    public gRPCServerBootstraper(
        IEnumerable<IGrpcDefinition> grpcDefinitions)
    {
        this._grpcDefinitions = grpcDefinitions;

        this.BootstrapFinished = new Subject<string>();
    }

    public void Shutdown()
    {
    }

    public Task Startup()
    {
        var rcpServer = new Grpc.Core.Server
        {
            Ports = { new ServerPort("0.0.0.0", 5000, ServerCredentials.Insecure) }
        };

        foreach (var grpcDefinition in this._grpcDefinitions)
        {
            grpcDefinition.AddGrpcService(rcpServer);
        }

        rcpServer.Start();

        this.BootstrapFinished.OnNext("gRPC");
        return Task.CompletedTask;
    }
}
