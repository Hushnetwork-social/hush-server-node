using HushNode.Caching;
using HushNode.Events;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using Olimpo;

namespace HushNode.Feeds;

public class FeedMessageTransactionHandler(
    IFeedMessageStorageService feedMessageStorageService,
    IBlockchainCache blockchainCache,
    IEventAggregator eventAggregator)
    : IFeedMessageTransactionHandler
{
    private readonly IFeedMessageStorageService _feedMessageStorageService = feedMessageStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IEventAggregator _eventAggregator = eventAggregator;

    public async Task HandleFeedMessageTransaction(ValidatedTransaction<NewFeedMessagePayload> validatedTransaction)
    {
        var issuerPublicAddress = validatedTransaction.UserSignature?.Signatory ?? string.Empty;

        // Validation: Warn if IssuerPublicAddress is empty
        if (string.IsNullOrEmpty(issuerPublicAddress))
        {
            Console.WriteLine($"[FeedMessageTransactionHandler] WARNING: UserSignature.Signatory is EMPTY for message {validatedTransaction.Payload.FeedMessageId}");
            Console.WriteLine($"[FeedMessageTransactionHandler] Transaction will be stored but message ownership detection will fail.");
            // Note: For now we still store the message, but in the future we could reject it
            // throw new InvalidOperationException("Transaction rejected: UserSignature.Signatory cannot be empty");
        }
        else
        {
            Console.WriteLine($"[FeedMessageTransactionHandler] IssuerPublicAddress: {issuerPublicAddress.Substring(0, Math.Min(30, issuerPublicAddress.Length))}...");
        }

        var feedMessage = new FeedMessage(
            validatedTransaction.Payload.FeedMessageId,
            validatedTransaction.Payload.FeedId,
            validatedTransaction.Payload.MessageContent,
            issuerPublicAddress,
            validatedTransaction.TransactionTimeStamp,
            this._blockchainCache.LastBlockIndex,
            ReplyToMessageId: validatedTransaction.Payload.ReplyToMessageId);

        await this._feedMessageStorageService.CreateFeedMessageAsync(feedMessage);

        // Publish event for notification system (via EventAggregator - no hard reference)
        await this._eventAggregator.PublishAsync(new NewFeedMessageCreatedEvent(feedMessage));
    }
}
