using System.Formats.Tar;
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

    public override Task<GetFeedForAddressReply> GetFeedsForAddress(GetFeedForAddressRequest request, ServerCallContext context)
    {
        var userHasFeeds = this._blockchainIndexDb.FeedsOfParticipant.ContainsKey(request.ProfilePublicKey);

        var reply = new GetFeedForAddressReply();

        if (userHasFeeds)
        {
            var feedIdsForUser = this._blockchainIndexDb.FeedsOfParticipant[request.ProfilePublicKey];
            foreach(var feedGuid in feedIdsForUser)
            {
                var feedDefinition =  this._blockchainIndexDb.Feeds
                    .SingleOrDefault(x => x.FeedId == feedGuid && x.BlockIndex > request.BlockIndex);

                if (feedDefinition == null)
                {

                }
                else
                {
                    var newFeed = new GetFeedForAddressReply.Types.Feed
                    {
                        FeedId = feedDefinition.FeedId,
                        FeedTitle = feedDefinition.FeedTitle,
                        FeedType = (int)feedDefinition.FeedType,
                        BlockIndex = (long)feedDefinition.BlockIndex
                    };
                    reply.Feeds.Add(newFeed);
                }
                
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

    public override Task<GetFeedMessagesForAddressReply> GetFeedMessagesForAddress(GetFeedMessagesForAddressRequest request, ServerCallContext context)
    {
        var reply = new GetFeedMessagesForAddressReply();
        var feedsForAddress = this._blockchainIndexDb.FeedsOfParticipant
            .Single(x => x.Key == request.ProfilePublicKey)
            .Value;

        foreach (var feed in feedsForAddress)
        {
            if (!this._blockchainIndexDb.FeedMessages.Any() || !this._blockchainIndexDb.FeedMessages[feed].Any())
            {
                // this means, in the feed there is no message
                continue;
            }

            var newMessagesInFeeds = this._blockchainIndexDb.FeedMessages[feed]
                .Where(x => x.BlockIndex > request.BlockIndex);   

            foreach (var newMessageInFeed in newMessagesInFeeds)
            {
                reply.Messages.Add(new GetFeedMessagesForAddressReply.Types.FeedMessage
                {
                    FeedId = newMessageInFeed.FeedId,
                    FeedMessageId = newMessageInFeed.FeedMessageId,
                    MessageContent = newMessageInFeed.MessageContent,
                    IssuerPublicAddress = newMessageInFeed.IssuerPublicAddress,
                    IssuerName = newMessageInFeed.IssuerName,
                    BlockIndex = newMessageInFeed.BlockIndex,
                    TimeStamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.SpecifyKind(newMessageInFeed.TimeStamp, DateTimeKind.Utc)),
                });
            }
        }

        return Task.FromResult(reply);
    }
    public override async Task<SendMessageReply> SendMessage(SendMessageRequest request, ServerCallContext context)
    {
        await this._eventAggregator.PublishAsync(new AddTrasactionToMemPoolEvent(
            new FeedMessage
            {
                FeedMessageId = request.FeedMessageId,
                FeedId = request.FeedId,
                Issuer = request.Issuer,
                Message = request.Message,
                TimeStamp = request.TimeStamp.ToDateTime(),
                Hash = request.Hash,
                Signature = request.Signature
            }
        ));

        return new SendMessageReply
        {
            Successfull = true,
            Message = "Feed message validated and added to the Mempool"
        };
    }
}
