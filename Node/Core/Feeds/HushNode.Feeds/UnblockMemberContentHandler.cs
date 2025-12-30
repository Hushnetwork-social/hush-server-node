using HushNode.Credentials;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Content handler for UnblockMember transactions.
/// Validates that:
/// - Sender is an admin in the group
/// - Target is currently blocked
/// - Group exists
/// </summary>
public class UnblockMemberContentHandler(
    ICredentialsProvider credentialProvider,
    IFeedsStorageService feedsStorageService)
    : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;

    public bool CanValidate(Guid transactionKind) =>
        UnblockMemberPayloadHandler.UnblockMemberPayloadKind == transactionKind;

    public AbstractTransaction ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<UnblockMemberPayload>;

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

        // Validation: Check group exists
        var groupFeed = this._feedsStorageService.GetGroupFeedAsync(payload.FeedId).GetAwaiter().GetResult();
        if (groupFeed == null)
        {
            return null!;
        }

        // Validation: Sender must be admin
        var isAdmin = this._feedsStorageService.IsAdminAsync(payload.FeedId, adminAddress).GetAwaiter().GetResult();
        if (!isAdmin)
        {
            return null!;
        }

        // Validation: Target must exist and be blocked
        var targetParticipant = this._feedsStorageService
            .GetGroupFeedParticipantAsync(payload.FeedId, payload.UnblockedUserPublicAddress)
            .GetAwaiter().GetResult();

        if (targetParticipant == null)
        {
            return null!; // Target is not a member
        }

        if (targetParticipant.ParticipantType != ParticipantType.Blocked)
        {
            return null!; // Target is not blocked
        }

        // All validations passed - sign the transaction
        var blockProducerCredentials = this._credentialProvider.GetCredentials();

        var signedByValidationTransaction = signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);

        return signedByValidationTransaction;
    }
}
