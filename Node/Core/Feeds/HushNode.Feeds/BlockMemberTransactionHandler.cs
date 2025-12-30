using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Handler for BlockMember transactions.
/// Sets participant status to Blocked.
/// Does NOT trigger key rotation - blocked member can still decrypt existing messages.
/// </summary>
public class BlockMemberTransactionHandler(
    IFeedsStorageService feedsStorageService)
    : IBlockMemberTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;

    public async Task HandleBlockMemberTransactionAsync(ValidatedTransaction<BlockMemberPayload> blockMemberTransaction)
    {
        var payload = blockMemberTransaction.Payload;

        // Update participant status from Member to Blocked
        // Note: NO key rotation - blocked member can still decrypt messages
        await this._feedsStorageService.UpdateParticipantTypeAsync(
            payload.FeedId,
            payload.BlockedUserPublicAddress,
            ParticipantType.Blocked);
    }
}
