using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class FeedMessageTransactionHandler(
    IFeedMessageStorageService feedMessageStorageService,
    IBlockchainCache blockchainCache) 
    : IFeedMessageTransactionHandler
{
    private readonly IFeedMessageStorageService _feedMessageStorageService = feedMessageStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;

    public async Task HandleFeedMessageTransaction(ValidatedTransaction<NewFeedMessagePayload> validatedTransaction)
    {
        var feedMessage = new FeedMessage(
            validatedTransaction.Payload.FeedMessageId,
            validatedTransaction.Payload.FeedId,
            validatedTransaction.Payload.MessageContent,
            validatedTransaction.UserSignature.Signatory,
            validatedTransaction.TransactionTimeStamp,
            this._blockchainCache.LastBlockIndex);

        await this._feedMessageStorageService.CreateFeedMessage(feedMessage);
    }
}
