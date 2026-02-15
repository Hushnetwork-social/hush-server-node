using Microsoft.Extensions.Logging;
using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Handler for UpdateGroupFeedTitle transactions.
/// Updates the group's title in PostgreSQL and cascades to feed_meta cache for all participants.
/// Does NOT trigger key rotation - metadata change only.
/// </summary>
public class UpdateGroupFeedTitleTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IFeedMetadataCacheService feedMetadataCacheService,
    ILogger<UpdateGroupFeedTitleTransactionHandler> logger)
    : IUpdateGroupFeedTitleTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IFeedMetadataCacheService _feedMetadataCacheService = feedMetadataCacheService;
    private readonly ILogger<UpdateGroupFeedTitleTransactionHandler> _logger = logger;

    public async Task HandleUpdateGroupFeedTitleTransactionAsync(ValidatedTransaction<UpdateGroupFeedTitlePayload> updateTitleTransaction)
    {
        var payload = updateTitleTransaction.Payload;

        // Update the group's title in PostgreSQL
        await this._feedsStorageService.UpdateGroupFeedTitleAsync(
            payload.FeedId,
            payload.NewTitle);

        // FEAT-065: Cascade title change to feed_meta cache for all group participants
        await CascadeGroupTitleChangeAsync(payload.FeedId, payload.NewTitle);
    }

    /// <summary>
    /// FEAT-065: Cascade group title change to all participants' feed_meta caches.
    /// Each participant's Hash entry for this feed gets its title updated.
    /// </summary>
    private async Task CascadeGroupTitleChangeAsync(FeedId feedId, string newTitle)
    {
        try
        {
            var participants = await this._feedsStorageService.GetAllParticipantsAsync(feedId);

            foreach (var participant in participants)
            {
                // Only update active participants (not left/banned)
                if (participant.LeftAtBlock == null)
                {
                    _ = this._feedMetadataCacheService.UpdateFeedTitleAsync(
                        participant.ParticipantPublicAddress, feedId, newTitle);
                }
            }

            this._logger.LogInformation(
                "Cascaded group title change for feed {FeedId} to {ParticipantCount} participants: \"{NewTitle}\"",
                feedId, participants.Count(p => p.LeftAtBlock == null), newTitle);
        }
        catch (Exception ex)
        {
            // Fire-and-forget: cache cascade failure should not block title update
            this._logger.LogWarning(ex,
                "Failed to cascade group title change for feed {FeedId}", feedId);
        }
    }
}
