using HushNode.Caching;
using HushNode.Events;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using Olimpo;

namespace HushNode.Feeds;

/// <summary>
/// Transaction handler for NewGroupFeedMessage transactions.
/// Stores the encrypted message and publishes notification event.
/// </summary>
public class NewGroupFeedMessageTransactionHandler(
    IFeedMessageStorageService feedMessageStorageService,
    IBlockchainCache blockchainCache,
    IEventAggregator eventAggregator)
    : INewGroupFeedMessageTransactionHandler
{
    private readonly IFeedMessageStorageService _feedMessageStorageService = feedMessageStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IEventAggregator _eventAggregator = eventAggregator;

    public async Task HandleGroupFeedMessageTransaction(ValidatedTransaction<NewGroupFeedMessagePayload> validatedTransaction)
    {
        var issuerPublicAddress = validatedTransaction.UserSignature?.Signatory ?? string.Empty;

        // Validation: Warn if IssuerPublicAddress is empty
        if (string.IsNullOrEmpty(issuerPublicAddress))
        {
            Console.WriteLine($"[NewGroupFeedMessageTransactionHandler] WARNING: UserSignature.Signatory is EMPTY for message {validatedTransaction.Payload.MessageId}");
            Console.WriteLine($"[NewGroupFeedMessageTransactionHandler] Transaction will be stored but message ownership detection will fail.");
        }
        else
        {
            Console.WriteLine($"[NewGroupFeedMessageTransactionHandler] IssuerPublicAddress: {issuerPublicAddress.Substring(0, Math.Min(30, issuerPublicAddress.Length))}...");
        }

        // Create FeedMessage - EncryptedContent is stored as MessageContent
        var feedMessage = new FeedMessage(
            validatedTransaction.Payload.MessageId,
            validatedTransaction.Payload.FeedId,
            validatedTransaction.Payload.EncryptedContent,
            issuerPublicAddress,
            validatedTransaction.TransactionTimeStamp,
            this._blockchainCache.LastBlockIndex,
            AuthorCommitment: validatedTransaction.Payload.AuthorCommitment,
            ReplyToMessageId: validatedTransaction.Payload.ReplyToMessageId,
            KeyGeneration: validatedTransaction.Payload.KeyGeneration);

        await this._feedMessageStorageService.CreateFeedMessageAsync(feedMessage);

        // Publish event for notification system (via EventAggregator - no hard reference)
        await this._eventAggregator.PublishAsync(new NewFeedMessageCreatedEvent(feedMessage));
    }
}
