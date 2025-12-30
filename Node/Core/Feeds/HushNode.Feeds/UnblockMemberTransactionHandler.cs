using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Handler for UnblockMember transactions.
/// Sets participant status back to Member.
/// Does NOT trigger key rotation.
/// </summary>
public class UnblockMemberTransactionHandler(
    IFeedsStorageService feedsStorageService)
    : IUnblockMemberTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;

    public async Task HandleUnblockMemberTransactionAsync(ValidatedTransaction<UnblockMemberPayload> unblockMemberTransaction)
    {
        var payload = unblockMemberTransaction.Payload;

        // Update participant status from Blocked back to Member
        await this._feedsStorageService.UpdateParticipantTypeAsync(
            payload.FeedId,
            payload.UnblockedUserPublicAddress,
            ParticipantType.Member);
    }
}
