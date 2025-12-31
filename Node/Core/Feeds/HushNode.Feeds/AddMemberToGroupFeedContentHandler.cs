using HushNode.Credentials;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Content handler that validates AddMemberToGroupFeed transactions.
/// Only admins can add members to a group.
/// Returns null on validation failure (existing pattern).
/// </summary>
public class AddMemberToGroupFeedContentHandler(
    ICredentialsProvider credentialProvider,
    IFeedsStorageService feedsStorageService)
    : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;

    public bool CanValidate(Guid transactionKind) =>
        AddMemberToGroupFeedPayloadHandler.AddMemberToGroupFeedPayloadKind == transactionKind;

    public AbstractTransaction ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<AddMemberToGroupFeedPayload>;

        if (signedTransaction == null)
        {
            return null!;
        }

        var payload = signedTransaction.Payload;
        var adminAddress = payload.AdminPublicAddress;
        var newMemberAddress = payload.NewMemberPublicAddress;

        // Validation: Admin address must match signatory
        var signatoryAddress = signedTransaction.UserSignature?.Signatory;
        if (string.IsNullOrEmpty(signatoryAddress) || signatoryAddress != adminAddress)
        {
            return null!;
        }

        // Validation: NewMemberPublicEncryptKey is required
        if (string.IsNullOrEmpty(payload.NewMemberPublicEncryptKey))
        {
            return null!;
        }

        // Get the group feed
        var groupFeed = this._feedsStorageService.GetGroupFeedAsync(payload.FeedId).GetAwaiter().GetResult();
        if (groupFeed == null)
        {
            return null!; // Group doesn't exist
        }

        // Validation: Cannot add to deleted group
        if (groupFeed.IsDeleted)
        {
            return null!;
        }

        // Validation: Sender must be an admin
        var isAdmin = this._feedsStorageService.IsAdminAsync(payload.FeedId, adminAddress).GetAwaiter().GetResult();
        if (!isAdmin)
        {
            return null!;
        }

        // Check if new member already exists
        var existingParticipant = this._feedsStorageService
            .GetParticipantWithHistoryAsync(payload.FeedId, newMemberAddress)
            .GetAwaiter().GetResult();

        if (existingParticipant != null)
        {
            // Validation: Cannot add banned user
            if (existingParticipant.ParticipantType == ParticipantType.Banned)
            {
                return null!;
            }

            // Validation: Cannot add if already an active member
            if (existingParticipant.LeftAtBlock == null)
            {
                return null!;
            }
        }

        // All validations passed - sign the transaction
        var blockProducerCredentials = this._credentialProvider.GetCredentials();

        var signedByValidationTransaction = signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);

        return signedByValidationTransaction;
    }
}
