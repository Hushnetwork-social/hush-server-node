using HushNode.Credentials;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Content handler for UpdateGroupFeedTitle transactions.
/// Validates that:
/// - Sender is an admin in the group
/// - New title is valid (non-empty, max 100 characters)
/// - Group exists and is not deleted
/// </summary>
public class UpdateGroupFeedTitleContentHandler(
    ICredentialsProvider credentialProvider,
    IFeedsStorageService feedsStorageService)
    : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;

    private const int MaxTitleLength = 100;

    public bool CanValidate(Guid transactionKind) =>
        UpdateGroupFeedTitlePayloadHandler.UpdateGroupFeedTitlePayloadKind == transactionKind;

    public AbstractTransaction ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<UpdateGroupFeedTitlePayload>;

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

        // Validation: New title must not be empty
        if (string.IsNullOrWhiteSpace(payload.NewTitle))
        {
            return null!;
        }

        // Validation: New title must be 100 characters or less
        if (payload.NewTitle.Length > MaxTitleLength)
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

        // All validations passed - sign the transaction
        var blockProducerCredentials = this._credentialProvider.GetCredentials();

        var signedByValidationTransaction = signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);

        return signedByValidationTransaction;
    }
}
