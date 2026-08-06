using HushNode.Credentials;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;

namespace HushNode.Identity;

public class FullIdentityContentHandler(
    ICredentialsProvider credentialProvider,
    IFullIdentityValidator validator) : ITransactionContentHandler, ITransactionValidationFailureReporter
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IFullIdentityValidator _validator = validator;
    private TransactionValidationFailure? _pendingFailure;

    public bool CanValidate(Guid transactionKind) =>
        FullIdentityPayloadHandler.FullIdentityPayloadKind == transactionKind;

    public AbstractTransaction? ValidateAndSign(AbstractTransaction transaction)
    {
        _pendingFailure = null;

        if (transaction is not SignedTransaction<FullIdentityPayload> fullIdentityTransaction)
        {
            _pendingFailure = new TransactionValidationFailure(
                FullIdentityValidationCodes.UnsupportedKind,
                "Transaction kind does not match the FullIdentity payload.");
            return null;
        }

        var validation = _validator.Validate(fullIdentityTransaction);
        if (!validation.IsValid)
        {
            // Expected validation failures are typed data (stable code), never
            // exceptions and never null-as-success without a reported code.
            _pendingFailure = new TransactionValidationFailure(
                validation.ValidationCode ?? FullIdentityValidationCodes.UnsupportedKind,
                validation.Message ?? "FullIdentity validation failed.");
            return null;
        }

        var blockProducerCredentials = _credentialProvider.GetCredentials();

        var signedByValidationTransaction = fullIdentityTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);

        return signedByValidationTransaction;
    }

    public bool TryTakeValidationFailure(Guid transactionId, out TransactionValidationFailure failure)
    {
        if (_pendingFailure is not null)
        {
            failure = _pendingFailure;
            _pendingFailure = null;
            return true;
        }

        failure = null!;
        return false;
    }
}
