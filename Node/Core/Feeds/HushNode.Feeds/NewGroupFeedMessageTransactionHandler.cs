using HushNode.Caching;
using HushNode.Events;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushNode.Feeds;

/// <summary>
/// Transaction handler for NewGroupFeedMessage transactions.
/// Stores the encrypted message and publishes notification event.
/// </summary>
public class NewGroupFeedMessageTransactionHandler(
    IFeedMessageStorageService feedMessageStorageService,
    IFeedMessageCacheService feedMessageCacheService,
    IBlockchainCache blockchainCache,
    IEventAggregator eventAggregator,
    ILogger<NewGroupFeedMessageTransactionHandler> logger)
    : INewGroupFeedMessageTransactionHandler
{
    private readonly IFeedMessageStorageService _feedMessageStorageService = feedMessageStorageService;
    private readonly IFeedMessageCacheService _feedMessageCacheService = feedMessageCacheService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IEventAggregator _eventAggregator = eventAggregator;
    private readonly ILogger<NewGroupFeedMessageTransactionHandler> _logger = logger;

    public async Task HandleGroupFeedMessageTransaction(ValidatedTransaction<NewGroupFeedMessagePayload> validatedTransaction)
    {
        var issuerPublicAddress = validatedTransaction.UserSignature?.Signatory ?? string.Empty;

        // Validation: Warn if IssuerPublicAddress is empty
        if (string.IsNullOrEmpty(issuerPublicAddress))
        {
            _logger.LogWarning(
                "UserSignature.Signatory is EMPTY for group message {MessageId}. Message ownership detection will fail.",
                validatedTransaction.Payload.MessageId);
        }
        else
        {
            _logger.LogDebug(
                "Processing group feed message from {IssuerAddress}",
                issuerPublicAddress.Substring(0, Math.Min(30, issuerPublicAddress.Length)));
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

        // Write to PostgreSQL (source of truth)
        await this._feedMessageStorageService.CreateFeedMessageAsync(feedMessage);

        // Write to Redis cache (FEAT-046: write-through pattern)
        // Log and continue on failure - Redis failure should not break the write path
        try
        {
            await this._feedMessageCacheService.AddMessageAsync(feedMessage.FeedId, feedMessage);
            _logger.LogDebug(
                "Cached group message {MessageId} for feed {FeedId}",
                feedMessage.FeedMessageId,
                feedMessage.FeedId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to cache group message {MessageId} for feed {FeedId}. PostgreSQL write succeeded.",
                feedMessage.FeedMessageId,
                feedMessage.FeedId);
        }

        // Publish event for notification system (fire-and-forget - don't block indexing)
        // Notifications are secondary to blockchain state and should not delay BlockIndexCompletedEvent
        _ = this._eventAggregator.PublishAsync(new NewFeedMessageCreatedEvent(feedMessage));
    }
}
