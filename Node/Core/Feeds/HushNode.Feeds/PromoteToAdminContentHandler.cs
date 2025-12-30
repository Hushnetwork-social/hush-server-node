using HushNode.Credentials;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Content handler for PromoteToAdmin transactions.
/// Validates that:
/// - Sender is an admin in the group
/// - Target is a regular member (not Blocked, not Banned, not Admin)
/// - Group exists
/// </summary>
public class PromoteToAdminContentHandler(
    ICredentialsProvider credentialProvider,
    IFeedsStorageService feedsStorageService)
    : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;

    public bool CanValidate(Guid transactionKind) =>
        PromoteToAdminPayloadHandler.PromoteToAdminPayloadKind == transactionKind;

    public AbstractTransaction ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<PromoteToAdminPayload>;

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

        // Validation: Cannot promote yourself
        if (adminAddress == payload.MemberPublicAddress)
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

        // Validation: Target must exist and be a regular member
        var targetParticipant = this._feedsStorageService
            .GetGroupFeedParticipantAsync(payload.FeedId, payload.MemberPublicAddress)
            .GetAwaiter().GetResult();

        if (targetParticipant == null)
        {
            return null!; // Target is not a member
        }

        // Can only promote regular Members (not Blocked, Banned, or already Admin)
        if (targetParticipant.ParticipantType != ParticipantType.Member)
        {
            return null!;
        }

        // All validations passed - sign the transaction
        var blockProducerCredentials = this._credentialProvider.GetCredentials();

        var signedByValidationTransaction = signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);

        return signedByValidationTransaction;
    }
}
