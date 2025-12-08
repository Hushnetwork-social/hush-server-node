using HushNode.Caching;
using HushNode.Credentials;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class NewChatFeedTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache) 
    : INewChatFeedTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;

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
        }

        this._feedsStorageService.CreateFeed(chatFeed);

        return Task.CompletedTask;
    }
}
