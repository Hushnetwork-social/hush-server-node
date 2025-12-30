using HushNode.Credentials;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Content handler for UpdateGroupFeedDescription transactions.
/// Validates that:
/// - Sender is an admin in the group
/// - Group exists and is not deleted
/// Note: Empty description is allowed (optional field).
/// </summary>
public class UpdateGroupFeedDescriptionContentHandler(
    ICredentialsProvider credentialProvider,
    IFeedsStorageService feedsStorageService)
    : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;

    public bool CanValidate(Guid transactionKind) =>
        UpdateGroupFeedDescriptionPayloadHandler.UpdateGroupFeedDescriptionPayloadKind == transactionKind;

    public AbstractTransaction ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<UpdateGroupFeedDescriptionPayload>;

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

        // Note: Empty description is allowed - description is optional

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

        // All validations passed - sign the transaction
        var blockProducerCredentials = this._credentialProvider.GetCredentials();

        var signedByValidationTransaction = signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);

        return signedByValidationTransaction;
    }
}
