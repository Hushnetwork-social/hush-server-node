using Microsoft.Extensions.Logging;
using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class NewPersonalFeedTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache,
    ILogger<NewPersonalFeedTransactionHandler> logger) : INewPersonalFeedTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly ILogger<NewPersonalFeedTransactionHandler> _logger = logger;

    public async Task HandleNewPersonalFeedTransactionAsync(ValidatedTransaction<NewPersonalFeedPayload> newPersonalFeedTransaction)
    {
        var newPersonalFeedPayload = newPersonalFeedTransaction.Payload;
        var signatoryAddress = newPersonalFeedTransaction.UserSignature.Signatory;

        // Validate required fields
        if (newPersonalFeedPayload.FeedId == FeedId.Empty)
        {
            this._logger.LogWarning("Rejecting NewPersonalFeed transaction: FeedId is empty. Signatory: {Signatory}", signatoryAddress);
            return;
        }

        if (string.IsNullOrWhiteSpace(newPersonalFeedPayload.EncryptedFeedKey))
        {
            this._logger.LogWarning("Rejecting NewPersonalFeed transaction: EncryptedFeedKey is null or empty. Signatory: {Signatory}", signatoryAddress);
            return;
        }

        var personalFeed = new Feed(
            newPersonalFeedPayload.FeedId,
            newPersonalFeedPayload.Title,
            newPersonalFeedPayload.FeedType,
            this._blockchainCache.LastBlockIndex);

        // The EncryptedFeedKey contains the feed's AES key encrypted with the owner's RSA public key
        var participant = new FeedParticipant
        (
            newPersonalFeedPayload.FeedId,
            signatoryAddress,
            ParticipantType.Owner,
            newPersonalFeedPayload.EncryptedFeedKey
        )
        {
            Feed = personalFeed
        };

        personalFeed.Participants.Add(participant);

        // Atomically check and create to prevent race conditions
        var created = await this._feedsStorageService.CreatePersonalFeedIfNotExists(personalFeed, signatoryAddress);

        if (created)
        {
            this._logger.LogInformation("Personal feed created: {FeedId} for {Signatory}", newPersonalFeedPayload.FeedId, signatoryAddress);
        }
        else
        {
            this._logger.LogWarning("Rejecting NewPersonalFeed transaction: User already has a personal feed. Signatory: {Signatory}", signatoryAddress);
        }
    }
}
