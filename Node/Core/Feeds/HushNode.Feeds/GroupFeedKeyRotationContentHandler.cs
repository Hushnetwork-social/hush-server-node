using HushNode.Credentials;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Content handler for validating and signing GroupFeedKeyRotation transactions.
/// Validates KeyGeneration sequence, encrypted keys structure, and signs valid transactions.
/// </summary>
public class GroupFeedKeyRotationContentHandler(
    ICredentialsProvider credentialProvider)
    : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;

    public bool CanValidate(Guid transactionKind) =>
        GroupFeedKeyRotationPayloadHandler.GroupFeedKeyRotationPayloadKind == transactionKind;

    public AbstractTransaction ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<GroupFeedKeyRotationPayload>;

        if (signedTransaction == null)
        {
            return null!;
        }

        var payload = signedTransaction.Payload;

        // Validation 1: NewKeyGeneration must be exactly PreviousKeyGeneration + 1
        if (payload.NewKeyGeneration != payload.PreviousKeyGeneration + 1)
        {
            return null!;
        }

        // Validation 2: NewKeyGeneration must be positive (KeyGeneration 0 is created with the group)
        if (payload.NewKeyGeneration <= 0)
        {
            return null!;
        }

        // Validation 3: EncryptedKeys array must not be empty
        if (payload.EncryptedKeys == null || payload.EncryptedKeys.Length == 0)
        {
            return null!;
        }

        // Validation 4: No duplicate member addresses in EncryptedKeys
        var addresses = payload.EncryptedKeys.Select(e => e.MemberPublicAddress).ToList();
        if (addresses.Distinct().Count() != addresses.Count)
        {
            return null!;
        }

        // Validation 5: All member addresses must be non-empty
        if (payload.EncryptedKeys.Any(e => string.IsNullOrWhiteSpace(e.MemberPublicAddress)))
        {
            return null!;
        }

        // Validation 6: All encrypted keys must be non-empty
        if (payload.EncryptedKeys.Any(e => string.IsNullOrWhiteSpace(e.EncryptedAesKey)))
        {
            return null!;
        }

        // Validation 7: FeedId must be valid
        if (payload.FeedId == default)
        {
            return null!;
        }

        // Validation 8: ValidFromBlock must be positive
        if (payload.ValidFromBlock <= 0)
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
