using System.Threading.Tasks;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.Credentials;
using HushNode.Feeds.Storage;
using HushNode.Identity;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;

namespace HushNode.Feeds.gRPC;

public class FeedsGrpcService(
    IFeedsStorageService feedsStorageService,
    IIdentityService identityService) : HushFeed.HushFeedBase
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IIdentityService _identityService = identityService;

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

    public override async Task<GetFeedForAddressReply> GetFeedsForAddress(GetFeedForAddressRequest request, ServerCallContext context)
    {
        var blockIndex = BlockIndexHandler.CreateNew(request.BlockIndex);

        var feeds = await this._feedsStorageService.GetFeedsForAddress(request.ProfilePublicKey, blockIndex);

        var reply = new GetFeedForAddressReply();
        foreach(var feed in feeds)
        {
            // TODO [AboimPinto] Here tghe FeedTitle should be calculated
            // * PersonalFeed -> ProfileAlias + (YOU) / First 10 characters of the public address + (YOU)
            // * ChatFeed -> Other chat participant ProfileAlias

            var feedAlias = feed.FeedType switch
            {
                FeedType.Personal => await ExtractPersonalFeedAlias(feed),
                FeedType.Chat => await ExtractChatFeedAlias(feed),
                FeedType.Broadcast => await ExtractBroascastAlias(feed),
                _ => throw new InvalidOperationException($"the FeedTYype {feed.FeedType} is not supported.")
            };

            reply.Feeds.Add(
                new GetFeedForAddressReply.Types.Feed
                {
                    FeedId = feed.FeedId.ToString(),
                    FeedTitle = feedAlias,
                    FeedType = (int)feed.FeedType,
                    BlockIndex = feed.BlockIndex.Value
                });
        }

        return reply;
    }

    private Task<string> ExtractBroascastAlias(Feed feed)
    {
        throw new NotImplementedException();
    }

    private Task<string> ExtractChatFeedAlias(Feed feed)
    {
        throw new NotImplementedException();
    }

    private async Task<string> ExtractPersonalFeedAlias(Feed feed)
    {
        var identity = await this._identityService.RetrieveIdentityAsync(feed.Participants.Single().ParticipantPublicAddress);

        return string.Format("{0} (YOU)", ((Profile)identity).Alias);
    }
}
