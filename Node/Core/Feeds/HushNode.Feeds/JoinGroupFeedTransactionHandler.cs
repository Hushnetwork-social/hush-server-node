using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Transaction handler that processes validated JoinGroupFeed transactions.
/// Adds the joining user as a participant to the group.
/// </summary>
public class JoinGroupFeedTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache)
    : IJoinGroupFeedTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;

    public async Task HandleJoinGroupFeedTransactionAsync(ValidatedTransaction<JoinGroupFeedPayload> transaction)
    {
        var payload = transaction.Payload;
        var currentBlock = this._blockchainCache.LastBlockIndex;
        var joiningUserAddress = payload.JoiningUserPublicAddress;

        // Check if this is a rejoin (user was previously a member)
        var existingParticipant = await this._feedsStorageService
            .GetParticipantWithHistoryAsync(payload.FeedId, joiningUserAddress);

        if (existingParticipant != null && existingParticipant.LeftAtBlock != null)
        {
            // Rejoin: Update existing participant
            await this._feedsStorageService.UpdateParticipantRejoinAsync(
                payload.FeedId,
                joiningUserAddress,
                currentBlock,
                ParticipantType.Member);
        }
        else if (existingParticipant == null)
        {
            // New join: Create new participant entity
            var newParticipant = new GroupFeedParticipantEntity(
                payload.FeedId,
                joiningUserAddress,
                ParticipantType.Member,
                currentBlock,
                LeftAtBlock: null,
                LastLeaveBlock: null);

            await this._feedsStorageService.AddParticipantAsync(payload.FeedId, newParticipant);
        }
        // Note: If existingParticipant != null && LeftAtBlock == null, they're already a member
        // This case should have been caught by validation, but we don't throw - just no-op

        // Key rotation note: The actual key rotation is triggered by the client
        // submitting a separate GroupFeedKeyRotationPayload transaction after joining.
        // This allows the admin/owner to generate and distribute the new encrypted keys.
    }
}
