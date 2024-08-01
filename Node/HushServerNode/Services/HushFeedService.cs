using Grpc.Core;
using HushEcosystem.Model.Blockchain;
using HushNetwork.proto;
using HushServerNode.Blockchain.Events;
using HushServerNode.CacheService;
using Olimpo;

namespace HushServerNode.Services;

public class HushFeedService : HushFeed.HushFeedBase
{
    private readonly IBlockchainCache _blockchainCache;
    private readonly IEventAggregator _eventAggregator;

    public HushFeedService(
        IBlockchainCache blockchainCache,
        IEventAggregator eventAggregator)
    {
        this._blockchainCache = blockchainCache;
        this._eventAggregator = eventAggregator;
    }

    public override Task<GetFeedForAddressReply> GetFeedsForAddress(GetFeedForAddressRequest request, ServerCallContext context)
    {
        // var userHasFeeds = this._blockchainIndexDb.FeedsOfParticipant.ContainsKey(request.ProfilePublicKey);
        var userHasFeeds = this._blockchainCache.UserHasFeeds(request.ProfilePublicKey);

        var reply = new GetFeedForAddressReply();

        if (userHasFeeds)
        {
            var feedIdsForUser = this._blockchainCache.GetUserFeeds(request.ProfilePublicKey);
            foreach(var feed in feedIdsForUser)
            {
                var newFeed = new GetFeedForAddressReply.Types.Feed
                {
                    FeedId = feed.FeedId,
                    FeedTitle = feed.Title,
                    FeedType = feed.FeedType,
                    BlockIndex = (long)feed.BlockIndex
                };

                foreach(var participant in feed.FeedParticipants)
                {
                    var participantProfile = this._blockchainCache.GetProfile(participant.ParticipantPublicAddress);

                    var feedParticipant = new GetFeedForAddressReply.Types.FeedParticipant
                    {
                        UserName = participantProfile?.UserName,
                        ParticipantPublicAddress = participant.ParticipantPublicAddress,
                        ParticipantType = participant.ParticipantType.ToString(),
                        PublicEncryptAddress = participant.PublicEncryptAddress,
                        PrivateEncryptKey = participant.PrivateEncryptKey,
                    };

                    newFeed.FeedParticipants.Add(feedParticipant);
                }
                
                reply.Feeds.Add(newFeed);
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
                FeedType = (int)request.PersonalFeed.FeedType,
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
                FeedType = (int)request.ParticipantOne.FeedType,
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
                FeedType = (int)request.ParticipantTwo.FeedType,
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

    public override Task<GetChatFeedParticipantsReply> GetChatFeedParticipants(GetChatFeedParticipantsRequest request, ServerCallContext context)
    {
        var reply = new GetChatFeedParticipantsReply();

        var feed = this._blockchainCache.GetFeed(request.FeedId);

        foreach (var participant in feed.FeedParticipants)
        {
            reply.ChatParticipants.Add(new GetChatFeedParticipantsReply.Types.ChatParticipant
            {
                FeedParticipantType = participant.ParticipantType,
                PublicSigningAddress = participant.ParticipantPublicAddress,
                PublicEncryptAddress = participant.PublicEncryptAddress,
            });
        }

        return Task.FromResult(reply);
    }

    public override Task<GetFeedMessagesForAddressReply> GetFeedMessagesForAddress(GetFeedMessagesForAddressRequest request, ServerCallContext context)
    {
        var reply = new GetFeedMessagesForAddressReply();

        var feedsForAddress = this._blockchainCache.GetUserFeeds(request.ProfilePublicKey);

        foreach (var feed in feedsForAddress)
        {
            var feedMessages = this._blockchainCache.GetFeedMessages(feed.FeedId, request.BlockIndex);

            foreach (var feedMessage in feedMessages)
            {
                reply.Messages.Add(new GetFeedMessagesForAddressReply.Types.FeedMessage
                {
                    FeedId = feedMessage.FeedId,
                    FeedMessageId = feedMessage.FeedMessageId,
                    MessageContent = feedMessage.MessageContent,
                    IssuerPublicAddress = feedMessage.IssuerPublicAddress,
                    IssuerName = feedMessage.IssuerName,
                    BlockIndex = feedMessage.BlockIndex,
                    TimeStamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.SpecifyKind(feedMessage.TimeStamp, DateTimeKind.Utc)),
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
