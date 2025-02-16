using Grpc.Core;
using HushNetwork.proto;
using HushServerNode.Interfaces;

namespace HushServerNode.InternalModule.Feed;

public class FeedGrpcServiceDefinition : IGrpcDefinition
{
    private readonly HushFeed.HushFeedBase _feedGrpcService;

    public FeedGrpcServiceDefinition(HushFeed.HushFeedBase feedGrpcService)
    {
        this._feedGrpcService = feedGrpcService;
    }

    public void AddGrpcService(Server server)
    {
        server.Services.Add(HushFeed.BindService(this._feedGrpcService));
    }
}
