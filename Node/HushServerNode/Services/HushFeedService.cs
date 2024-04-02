using Grpc.Core;
using HushEcosystem.Model;
using HushEcosystem.Model.Blockchain;
using HushNetwork.proto;
using HushServerNode.Blockchain;
using HushServerNode.Blockchain.Events;
using Olimpo;

namespace HushServerNode.Services;

public class HushFeedService : HushFeed.HushFeedBase
{
    private readonly IBlockchainIndexDb _blockchainIndexDb;
        private readonly IEventAggregator _eventAggregator;

    public HushFeedService(
        IBlockchainIndexDb blockchainIndexDb,
        IEventAggregator eventAggregator)
    {
        this._blockchainIndexDb = blockchainIndexDb;
        this._eventAggregator = eventAggregator;
    }

    public override Task<GetFeedByAddressReply> GetFeedByAddress(GetFeedByAddressRequest request, ServerCallContext context)
    {
        var userHasFeeds = this._blockchainIndexDb.FeedsOfParticipant.ContainsKey(request.ProfilePublicKey);

        var reply = new GetFeedByAddressReply();

        if (userHasFeeds)
        {
            var feedIdsForUser = this._blockchainIndexDb.FeedsOfParticipant[request.ProfilePublicKey];
            foreach(var feedGuid in feedIdsForUser)
            {
                var feedDefinition =  this._blockchainIndexDb.Feeds.Single(x => x.FeedId == feedGuid);

                var newFeed = new GetFeedByAddressReply.Types.Feed
                {
                    FeedId = feedDefinition.FeedId,
                    FeedTitle = feedDefinition.FeedTitle,
                    FeedType = (int)feedDefinition.FeedType,
                    BlockIndex = (long)feedDefinition.BlockIndex
                };
                reply.Feeds.Add(newFeed);
            }
        }

        return Task.FromResult(reply);
    }

    public override Task<CreateFeedReply> CreateFeed(CreateFeedRequest request, ServerCallContext context)
    {
        this._eventAggregator.PublishAsync(new AddTrasactionToMemPoolEvent(
                new Feed
                {
                    FeedId = request.FeedId,
                    FeedType = request.FeedType.ToFeedTypeEnum(),
                    Issuer = request.Issuer,
                    FeedParticipantPublicAddress = request.FeedParticipantPublicAddress,
                    FeedPublicEncriptAddress = request.FeedPublicEncriptAddress,
                    FeedPrivateEncriptAddress = request.FeedPrivateEncriptAddress,
                    Hash = request.Hash,
                    Signature = request.Signature
                }
            ));

        return Task.FromResult(new CreateFeedReply
        {
            Successfull = true,
            Message = "Feed validated and added to the Mempool"
        });
    }
}
