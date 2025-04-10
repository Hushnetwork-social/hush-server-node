using HushNode.Caching;
using HushNode.Credentials;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class NewFeedTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache,
    ICredentialsProvider credentialsProvider) : INewFeedTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly ICredentialsProvider _credentialsProvider = credentialsProvider;

    public Task HandleNewPersonalFeedTransactionAsync(ValidatedTransaction<NewPersonalFeedPayload> newPersonalFeedTransaction)
    {
        var newPersonalFeedPayload = newPersonalFeedTransaction.Payload;

        var personalFeed = new Feed(
            newPersonalFeedPayload.FeedId,
            newPersonalFeedPayload.Title,
            newPersonalFeedPayload.FeedType,
            this._blockchainCache.LastBlockIndex);

        var credentials = this._credentialsProvider.GetCredentials();

        var participant = new FeedParticipant
        (
            newPersonalFeedPayload.FeedId,
            newPersonalFeedTransaction.UserSignature.Signatory,
            ParticipantType.Owner,
            credentials.PublicSigningAddress,
            credentials.PublicEncryptAddress
        );

        personalFeed.Participants.Add(participant);

        this._feedsStorageService.CreateFeed(personalFeed);

        return Task.CompletedTask;
    }
}
