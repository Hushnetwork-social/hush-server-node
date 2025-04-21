using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class NewPersonalFeedTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache) : INewPersonalFeedTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;

    public Task HandleNewPersonalFeedTransactionAsync(ValidatedTransaction<NewPersonalFeedPayload> newPersonalFeedTransaction)
    {
        var newPersonalFeedPayload = newPersonalFeedTransaction.Payload;

        var personalFeed = new Feed(
            newPersonalFeedPayload.FeedId,
            newPersonalFeedPayload.Title,
            newPersonalFeedPayload.FeedType,
            this._blockchainCache.LastBlockIndex);

        // The PersonalFeed encrypt keys are the one from the user. It's not necessary to save it. Even encrypt.
        var participant = new FeedParticipant
        (
            newPersonalFeedPayload.FeedId,
            newPersonalFeedTransaction.UserSignature.Signatory,
            ParticipantType.Owner,
            string.Empty,
            string.Empty
        )
        {
            Feed = personalFeed
        };

        personalFeed.Participants.Add(participant);

        this._feedsStorageService.CreateFeed(personalFeed);

        return Task.CompletedTask;
    }
}
