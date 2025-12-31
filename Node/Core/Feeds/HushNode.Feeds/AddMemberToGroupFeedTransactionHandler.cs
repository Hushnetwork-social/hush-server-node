using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Transaction handler that processes validated AddMemberToGroupFeed transactions.
/// Adds the new member as a participant to the group.
/// </summary>
public class AddMemberToGroupFeedTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache)
    : IAddMemberToGroupFeedTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;

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

        // Key rotation note: The admin should submit a GroupFeedKeyRotationPayload
        // transaction to distribute the new encrypted keys to all members including
        // the new one. The NewMemberPublicEncryptKey in the payload can be used to
        // encrypt the feed key for the new member.
    }
}
