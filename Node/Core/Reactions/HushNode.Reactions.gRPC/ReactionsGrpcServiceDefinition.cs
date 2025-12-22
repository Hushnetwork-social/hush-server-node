using Grpc.Core;
using HushNetwork.proto;
using HushNode.Interfaces;

namespace HushNode.Reactions.gRPC;

public class ReactionsGrpcServiceDefinition(HushReactions.HushReactionsBase hushReactionsGrpcService) : IGrpcDefinition
{
    private readonly HushReactions.HushReactionsBase _hushReactionsGrpcService = hushReactionsGrpcService;

    public void AddGrpcService(Server server)
    {
        server.Services.Add(HushReactions.BindService(this._hushReactionsGrpcService));
    }
}
