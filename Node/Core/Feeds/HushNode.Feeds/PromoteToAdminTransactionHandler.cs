using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Handler for PromoteToAdmin transactions.
/// Changes participant type from Member to Admin.
/// Does NOT trigger key rotation - same keys, different permissions.
/// </summary>
public class PromoteToAdminTransactionHandler(
    IFeedsStorageService feedsStorageService)
    : IPromoteToAdminTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;

    public async Task HandlePromoteToAdminTransactionAsync(ValidatedTransaction<PromoteToAdminPayload> promoteToAdminTransaction)
    {
        var payload = promoteToAdminTransaction.Payload;

        // Update participant type from Member to Admin
        // Note: NO key rotation - promoted member uses same keys with new permissions
        await this._feedsStorageService.UpdateParticipantTypeAsync(
            payload.FeedId,
            payload.MemberPublicAddress,
            ParticipantType.Admin);
    }
}
