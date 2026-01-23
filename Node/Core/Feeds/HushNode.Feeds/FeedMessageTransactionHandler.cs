using HushNode.Caching;
using HushNode.Events;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushNode.Feeds;

public class FeedMessageTransactionHandler(
    IFeedMessageStorageService feedMessageStorageService,
    IFeedMessageCacheService feedMessageCacheService,
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache,
    IEventAggregator eventAggregator,
    ILogger<FeedMessageTransactionHandler> logger)
    : IFeedMessageTransactionHandler
{
    private readonly IFeedMessageStorageService _feedMessageStorageService = feedMessageStorageService;
    private readonly IFeedMessageCacheService _feedMessageCacheService = feedMessageCacheService;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IEventAggregator _eventAggregator = eventAggregator;
    private readonly ILogger<FeedMessageTransactionHandler> _logger = logger;

    public async Task HandleFeedMessageTransaction(ValidatedTransaction<NewFeedMessagePayload> validatedTransaction)
    {
        var issuerPublicAddress = validatedTransaction.UserSignature?.Signatory ?? string.Empty;

        // Validation: Warn if IssuerPublicAddress is empty
        if (string.IsNullOrEmpty(issuerPublicAddress))
        {
            _logger.LogWarning(
                "UserSignature.Signatory is EMPTY for message {MessageId}. Message ownership detection will fail.",
                validatedTransaction.Payload.FeedMessageId);
        }
        else
        {
            _logger.LogDebug(
                "Processing feed message from {IssuerAddress}",
                issuerPublicAddress.Substring(0, Math.Min(30, issuerPublicAddress.Length)));
        }

        var feedMessage = new FeedMessage(
            validatedTransaction.Payload.FeedMessageId,
            validatedTransaction.Payload.FeedId,
            validatedTransaction.Payload.MessageContent,
            issuerPublicAddress,
            validatedTransaction.TransactionTimeStamp,
            this._blockchainCache.LastBlockIndex,
            ReplyToMessageId: validatedTransaction.Payload.ReplyToMessageId);

        // Write to PostgreSQL (source of truth)
        await this._feedMessageStorageService.CreateFeedMessageAsync(feedMessage);

        // Update the feed's BlockIndex so clients know there's new content
        // This is critical for sync - clients filter by BlockIndex, so if the feed's
        // BlockIndex isn't updated, new messages may be filtered out
        await this._feedsStorageService.UpdateFeedBlockIndexAsync(
            feedMessage.FeedId,
            this._blockchainCache.LastBlockIndex);

        _logger.LogDebug(
            "Updated feed {FeedId} BlockIndex to {BlockIndex} after new message",
            feedMessage.FeedId,
            this._blockchainCache.LastBlockIndex.Value);

        // Write to Redis cache (FEAT-046: write-through pattern)
        // Log and continue on failure - Redis failure should not break the write path
        try
        {
            await this._feedMessageCacheService.AddMessageAsync(feedMessage.FeedId, feedMessage);
            _logger.LogDebug(
                "Cached message {MessageId} for feed {FeedId}",
                feedMessage.FeedMessageId,
                feedMessage.FeedId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to cache message {MessageId} for feed {FeedId}. PostgreSQL write succeeded.",
                feedMessage.FeedMessageId,
                feedMessage.FeedId);
        }

        // Publish event for notification system (fire-and-forget - don't block indexing)
        // Notifications are secondary to blockchain state and should not delay BlockIndexCompletedEvent
        _ = this._eventAggregator.PublishAsync(new NewFeedMessageCreatedEvent(feedMessage));
    }
}
