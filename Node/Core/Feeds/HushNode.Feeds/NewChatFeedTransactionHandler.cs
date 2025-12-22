using HushNode.Caching;
using HushNode.Credentials;
using HushNode.Feeds.Events;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using Olimpo;

namespace HushNode.Feeds;

public class NewChatFeedTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache,
    IEventAggregator eventAggregator)
    : INewChatFeedTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IEventAggregator _eventAggregator = eventAggregator;

    public Task HandleNewChatFeedTransactionAsync(ValidatedTransaction<NewChatFeedPayload> newChatFeedTransaction)
    {
        var newChatFeedPayload = newChatFeedTransaction.Payload;

        var chatFeed = new Feed(
            newChatFeedPayload.FeedId,
            string.Empty,
            newChatFeedPayload.FeedType,
            this._blockchainCache.LastBlockIndex);

        if (newChatFeedPayload.FeedParticipants.Length != 2)
        {
            throw new InvalidOperationException("Cannot create a Chat Feed with less or more than 2 participants.");
        }

        var participantAddresses = new List<string>();

        foreach(var feedParticipant in newChatFeedPayload.FeedParticipants)
        {
            var participant = new FeedParticipant
            (
                newChatFeedPayload.FeedId,
                feedParticipant.ParticipantPublicAddress,
                ParticipantType.Owner,
                feedParticipant.EncryptedFeedKey
            )
            {
                Feed = chatFeed
            };

            chatFeed.Participants.Add(participant);
            participantAddresses.Add(feedParticipant.ParticipantPublicAddress);
        }

        this._feedsStorageService.CreateFeed(chatFeed);

        // Publish event for other modules (e.g., Reactions) to handle
        // Fire and forget - don't block feed creation on reaction setup
        _ = this._eventAggregator.PublishAsync(new FeedCreatedEvent(
            newChatFeedPayload.FeedId,
            participantAddresses.ToArray(),
            newChatFeedPayload.FeedType));

        return Task.CompletedTask;
    }
}
