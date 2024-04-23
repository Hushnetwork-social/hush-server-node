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
                    .SingleOrDefault(x => 
                        x.FeedId == feedGuid && 
                        x.BlockIndex > request.BlockIndex &&
                        x.FeedParticipant == request.ProfilePublicKey);

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

    public override Task<CreatePersonalFeedReply> CreatePersonalFeed(CreatePersonalFeedRequest request, ServerCallContext context)
    {
        this._eventAggregator.PublishAsync(new AddTrasactionToMemPoolEvent(
            new Feed
            {
                FeedId = request.PersonalFeed.FeedId,
                FeedType = request.PersonalFeed.FeedType.ToFeedTypeEnum(),
                Issuer = request.PersonalFeed.Issuer,
                FeedParticipantPublicAddress = request.PersonalFeed.FeedParticipantPublicAddress,
                FeedPublicEncriptAddress = request.PersonalFeed.FeedPublicEncriptAddress,
                FeedPrivateEncriptAddress = request.PersonalFeed.FeedPrivateEncriptAddress,
                Hash = request.PersonalFeed.Hash,
                Signature = request.PersonalFeed.Signature
            }
        ));

        return Task.FromResult(new CreatePersonalFeedReply
        {
            Successfull = true,
            Message = "Feed validated and added to the Mempool"
        });
    }

    public override Task<CreateChatFeedReply> CreateChatFeed(CreateChatFeedRequest request, ServerCallContext context)
    {
        // TODO [AboimPinto] Need to validate if the feed can be created. If the any of the participants had blocked the other or other rules.

        this._eventAggregator.PublishAsync(new AddTrasactionToMemPoolEvent(
            new Feed
            {
                FeedId = request.ParticipantOne.FeedId,
                FeedType = request.ParticipantOne.FeedType.ToFeedTypeEnum(),
                Issuer = request.ParticipantOne.Issuer,
                FeedParticipantPublicAddress = request.ParticipantOne.FeedParticipantPublicAddress,
                FeedPublicEncriptAddress = request.ParticipantOne.FeedPublicEncriptAddress,
                FeedPrivateEncriptAddress = request.ParticipantOne.FeedPrivateEncriptAddress,
                Hash = request.ParticipantOne.Hash,
                Signature = request.ParticipantOne.Signature
            }
        ));

        this._eventAggregator.PublishAsync(new AddTrasactionToMemPoolEvent(
            new Feed
            {
                FeedId = request.ParticipantTwo.FeedId,
                FeedType = request.ParticipantTwo.FeedType.ToFeedTypeEnum(),
                Issuer = request.ParticipantTwo.Issuer,
                FeedParticipantPublicAddress = request.ParticipantTwo.FeedParticipantPublicAddress,
                FeedPublicEncriptAddress = request.ParticipantTwo.FeedPublicEncriptAddress,
                FeedPrivateEncriptAddress = request.ParticipantTwo.FeedPrivateEncriptAddress,
                Hash = request.ParticipantTwo.Hash,
                Signature = request.ParticipantTwo.Signature
            }
        ));

        return Task.FromResult(new CreateChatFeedReply
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
            if (!this._blockchainIndexDb.FeedMessages.Any(x => x.Key == feed))
            {
                continue;
            }

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
