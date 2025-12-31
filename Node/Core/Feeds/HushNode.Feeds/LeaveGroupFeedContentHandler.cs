using HushNode.Credentials;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Content handler that validates LeaveGroupFeed transactions.
/// Returns null on validation failure (existing pattern).
/// </summary>
public class LeaveGroupFeedContentHandler(
    ICredentialsProvider credentialProvider,
    IFeedsStorageService feedsStorageService)
    : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;

    public bool CanValidate(Guid transactionKind) =>
        LeaveGroupFeedPayloadHandler.LeaveGroupFeedPayloadKind == transactionKind;

    public AbstractTransaction ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<LeaveGroupFeedPayload>;

        if (signedTransaction == null)
        {
            return null!;
        }

        var payload = signedTransaction.Payload;
        var leavingUserAddress = payload.LeavingUserPublicAddress;

        // Validation: Leaving user address must match signatory
        var signatoryAddress = signedTransaction.UserSignature?.Signatory;
        if (string.IsNullOrEmpty(signatoryAddress) || signatoryAddress != leavingUserAddress)
        {
            return null!;
        }

        // Get the group feed
        var groupFeed = this._feedsStorageService.GetGroupFeedAsync(payload.FeedId).GetAwaiter().GetResult();
        if (groupFeed == null)
        {
            return null!; // Group doesn't exist
        }

        // Validation: Cannot leave deleted group
        if (groupFeed.IsDeleted)
        {
            return null!;
        }

        // Get participant (only active members)
        var participant = this._feedsStorageService
            .GetGroupFeedParticipantAsync(payload.FeedId, leavingUserAddress)
            .GetAwaiter().GetResult();

        // Validation: Must be an active member to leave
        if (participant == null)
        {
            return null!; // Not a member
        }

        // Validation: Banned users cannot leave (they've been removed)
        if (participant.ParticipantType == ParticipantType.Banned)
        {
            return null!;
        }

        // Validation: Already left (this shouldn't happen with GetGroupFeedParticipantAsync which filters on LeftAtBlock == null)
        if (participant.LeftAtBlock != null)
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
