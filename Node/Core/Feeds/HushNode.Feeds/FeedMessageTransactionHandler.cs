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
    IFeedMetadataCacheService feedMetadataCacheService,
    IFeedParticipantsCacheService feedParticipantsCacheService,
    IBlockchainCache blockchainCache,
    IAttachmentTempStorageService attachmentTempStorageService,
    IAttachmentStorageService attachmentStorageService,
    IEventAggregator eventAggregator,
    ILogger<FeedMessageTransactionHandler> logger)
    : IFeedMessageTransactionHandler
{
    private readonly IFeedMessageStorageService _feedMessageStorageService = feedMessageStorageService;
    private readonly IFeedMessageCacheService _feedMessageCacheService = feedMessageCacheService;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IFeedMetadataCacheService _feedMetadataCacheService = feedMetadataCacheService;
    private readonly IFeedParticipantsCacheService _feedParticipantsCacheService = feedParticipantsCacheService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IAttachmentTempStorageService _attachmentTempStorageService = attachmentTempStorageService;
    private readonly IAttachmentStorageService _attachmentStorageService = attachmentStorageService;
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
            AuthorCommitment: validatedTransaction.Payload.AuthorCommitment,
            ReplyToMessageId: validatedTransaction.Payload.ReplyToMessageId,
            KeyGeneration: validatedTransaction.Payload.KeyGeneration);

        // Write to PostgreSQL (source of truth)
        await this._feedMessageStorageService.CreateFeedMessageAsync(feedMessage);

        // FEAT-066: Persist attachments from temp storage to PostgreSQL
        if (validatedTransaction.Payload.Attachments is { Count: > 0 })
        {
            foreach (var attachmentRef in validatedTransaction.Payload.Attachments)
            {
                try
                {
                    var tempData = await this._attachmentTempStorageService.RetrieveAsync(attachmentRef.Id);
                    if (tempData is null)
                    {
                        _logger.LogWarning(
                            "Temp file missing for attachment {AttachmentId} on message {MessageId}. Skipping.",
                            attachmentRef.Id, feedMessage.FeedMessageId);
                        continue;
                    }

                    var entity = new AttachmentEntity(
                        attachmentRef.Id,
                        tempData.Value.EncryptedOriginal!,
                        tempData.Value.EncryptedThumbnail,
                        feedMessage.FeedMessageId,
                        attachmentRef.Size,
                        tempData.Value.EncryptedThumbnail?.Length ?? 0,
                        attachmentRef.MimeType,
                        attachmentRef.FileName,
                        attachmentRef.Hash,
                        DateTime.UtcNow);

                    await this._attachmentStorageService.CreateAttachmentAsync(entity);
                    await this._attachmentTempStorageService.DeleteAsync(attachmentRef.Id);

                    _logger.LogDebug(
                        "Persisted attachment {AttachmentId} ({MimeType}, {Size} bytes) for message {MessageId}",
                        attachmentRef.Id, attachmentRef.MimeType, attachmentRef.Size, feedMessage.FeedMessageId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to persist attachment {AttachmentId} for message {MessageId}. Message write succeeded.",
                        attachmentRef.Id, feedMessage.FeedMessageId);
                }
            }
        }

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

        // FEAT-065: Update feed_meta lastBlockIndex for all participants (read-modify-write)
        // Fire-and-forget: Redis failure should not block message finalization
        // If entry doesn't exist (cache miss), UpdateLastBlockIndexAsync skips silently
        try
        {
            var participants = await this._feedParticipantsCacheService.GetParticipantsAsync(feedMessage.FeedId);
            if (participants != null)
            {
                foreach (var participantAddress in participants)
                {
                    _ = this._feedMetadataCacheService.UpdateLastBlockIndexAsync(
                        participantAddress, feedMessage.FeedId, this._blockchainCache.LastBlockIndex);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to update feed_meta for feed {FeedId}. PostgreSQL is consistent.",
                feedMessage.FeedId);
        }

        // Publish event for notification system (fire-and-forget - don't block indexing)
        // Notifications are secondary to blockchain state and should not delay BlockIndexCompletedEvent
        _ = this._eventAggregator.PublishAsync(new NewFeedMessageCreatedEvent(feedMessage));
    }
}
