using HushNode.Credentials;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class NewGroupFeedContentHandler(
    ICredentialsProvider credentialProvider)
    : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;

    public bool CanValidate(Guid transactionKind) =>
        NewGroupFeedPayloadHandler.NewGroupFeedPayloadKind == transactionKind;

    public AbstractTransaction ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<NewGroupFeedPayload>;

        if (signedTransaction == null)
        {
            return null!;
        }

        var payload = signedTransaction.Payload;
        var creatorAddress = signedTransaction.UserSignature?.Signatory;

        // Validation: Group title is required
        if (string.IsNullOrWhiteSpace(payload.Title))
        {
            return null!;
        }

        // Validation: Title must be 100 characters or less
        if (payload.Title.Length > 100)
        {
            return null!;
        }

        // Validation: At least 1 participant required
        if (payload.Participants == null || payload.Participants.Length == 0)
        {
            return null!;
        }

        // Validation: Creator must be in participant list
        if (string.IsNullOrEmpty(creatorAddress) ||
            !payload.Participants.Any(p => p.ParticipantPublicAddress == creatorAddress))
        {
            return null!;
        }

        // Validation: No duplicate participant addresses
        var addresses = payload.Participants.Select(p => p.ParticipantPublicAddress).ToList();
        if (addresses.Distinct().Count() != addresses.Count)
        {
            return null!;
        }

        // Validation: All participant addresses must be non-empty
        if (payload.Participants.Any(p => string.IsNullOrWhiteSpace(p.ParticipantPublicAddress)))
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
