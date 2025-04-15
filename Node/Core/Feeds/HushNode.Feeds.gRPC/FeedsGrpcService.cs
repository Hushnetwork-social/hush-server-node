using Grpc.Core;
using HushNetwork.proto;
using HushNode.Feeds.Storage;

namespace HushNode.Feeds.gRPC;

public class FeedsGrpcService(IFeedsStorageService feedsStorageService) : HushFeed.HushFeedBase
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;

    public override async Task<HasPersonalFeedReply> HasPersonalFeed(
        HasPersonalFeedRequest request, 
        ServerCallContext context)
    {
        var hasPersonalFeed = await this._feedsStorageService.HasPersonalFeed(request.PublicPublicKey);

        return new HasPersonalFeedReply
        {
            FeedAvailable = hasPersonalFeed
        };
    }
}
