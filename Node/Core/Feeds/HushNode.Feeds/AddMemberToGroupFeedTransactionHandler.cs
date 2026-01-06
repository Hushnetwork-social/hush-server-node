using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Transaction handler that processes validated AddMemberToGroupFeed transactions.
/// Adds the new member as a participant to the group and triggers key rotation
/// to distribute encryption keys to the new member.
/// </summary>
public class AddMemberToGroupFeedTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache,
    IKeyRotationService keyRotationService)
    : IAddMemberToGroupFeedTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IKeyRotationService _keyRotationService = keyRotationService;

    public async Task HandleAddMemberToGroupFeedTransactionAsync(ValidatedTransaction<AddMemberToGroupFeedPayload> transaction)
    {
        var payload = transaction.Payload;
        var currentBlock = this._blockchainCache.LastBlockIndex;
        var newMemberAddress = payload.NewMemberPublicAddress;

        // Check if this is a re-add (user was previously a member and left)
        var existingParticipant = await this._feedsStorageService
            .GetParticipantWithHistoryAsync(payload.FeedId, newMemberAddress);

        if (existingParticipant != null && existingParticipant.LeftAtBlock != null)
        {
            // Re-add: Update existing participant
            await this._feedsStorageService.UpdateParticipantRejoinAsync(
                payload.FeedId,
                newMemberAddress,
                currentBlock,
                ParticipantType.Member);
        }
        else if (existingParticipant == null)
        {
            // New member: Create new participant entity
            var newParticipant = new GroupFeedParticipantEntity(
                payload.FeedId,
                newMemberAddress,
                ParticipantType.Member,
                currentBlock,
                LeftAtBlock: null,
                LastLeaveBlock: null);

            await this._feedsStorageService.AddParticipantAsync(payload.FeedId, newParticipant);
        }
        // Note: If existingParticipant != null && LeftAtBlock == null, they're already a member
        // This case should have been caught by validation, but we don't throw - just no-op

        // Trigger key rotation to distribute encryption keys to the new member
        // This ensures the new member immediately receives keys to send and receive messages
        await this._keyRotationService.TriggerAndPersistRotationAsync(
            payload.FeedId,
            RotationTrigger.Join,
            joiningMemberAddress: newMemberAddress,
            leavingMemberAddress: null);
    }
}
