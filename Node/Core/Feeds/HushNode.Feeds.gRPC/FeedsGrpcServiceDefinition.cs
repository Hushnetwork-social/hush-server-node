using Grpc.Core;
using HushNetwork.proto;
using HushNode.Interfaces;

namespace HushNode.Feeds.gRPC;

public class FeedsGrpcServiceDefinition(HushFeed.HushFeedBase hushFeedGrpcService) : IGrpcDefinition
{
    private readonly HushFeed.HushFeedBase _hushFeedGrpcService = hushFeedGrpcService;

    public void AddGrpcService(Server server)
    {
        server.Services.Add(HushFeed.BindService(this._hushFeedGrpcService));
    }
}
