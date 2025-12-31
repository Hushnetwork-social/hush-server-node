using HushNode.Credentials;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Content handler for BanFromGroupFeed transactions.
/// Validates that:
/// - Sender is an admin in the group
/// - Target is a member or blocked (not admin)
/// - Target is not already banned
/// - Group exists and is not deleted
/// - Cannot ban yourself
/// </summary>
public class BanFromGroupFeedContentHandler(
    ICredentialsProvider credentialProvider,
    IFeedsStorageService feedsStorageService)
    : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;

    public bool CanValidate(Guid transactionKind) =>
        BanFromGroupFeedPayloadHandler.BanFromGroupFeedPayloadKind == transactionKind;

    public AbstractTransaction ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<BanFromGroupFeedPayload>;

        if (signedTransaction == null)
        {
            return null!;
        }

        var payload = signedTransaction.Payload;
        var adminAddress = signedTransaction.UserSignature?.Signatory;

        // Validation: Admin address is required
        if (string.IsNullOrEmpty(adminAddress))
        {
            return null!;
        }

        // Validation: Cannot ban yourself
        if (adminAddress == payload.BannedUserPublicAddress)
        {
            return null!;
        }

        // Validation: Check group exists and is not deleted
        var groupFeed = this._feedsStorageService.GetGroupFeedAsync(payload.FeedId).GetAwaiter().GetResult();
        if (groupFeed == null || groupFeed.IsDeleted)
        {
            return null!;
        }

        // Validation: Sender must be admin
        var isAdmin = this._feedsStorageService.IsAdminAsync(payload.FeedId, adminAddress).GetAwaiter().GetResult();
        if (!isAdmin)
        {
            return null!;
        }

        // Validation: Target must exist and be a member or blocked (not admin, not already banned)
        var targetParticipant = this._feedsStorageService
            .GetGroupFeedParticipantAsync(payload.FeedId, payload.BannedUserPublicAddress)
            .GetAwaiter().GetResult();

        if (targetParticipant == null)
        {
            return null!; // Target is not a participant
        }

        if (targetParticipant.ParticipantType == ParticipantType.Admin)
        {
            return null!; // Cannot ban an admin
        }

        if (targetParticipant.ParticipantType == ParticipantType.Banned)
        {
            return null!; // Already banned
        }

        // Valid states to ban from: Member or Blocked
        if (targetParticipant.ParticipantType != ParticipantType.Member &&
            targetParticipant.ParticipantType != ParticipantType.Blocked)
        {
            return null!; // Invalid state for ban
        }

        // All validations passed - sign the transaction
        var blockProducerCredentials = this._credentialProvider.GetCredentials();

        var signedByValidationTransaction = signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);

        return signedByValidationTransaction;
    }
}
