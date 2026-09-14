// FEAT-015 Task 6.3 — licence content handler.
//
// Mirrors FullIdentityContentHandler: after the admission gate has validated + reserved, this
// handler re-validates the exact signed licence transaction (independent of the generic permissive
// helper), signs it with the block-producer credentials, and reports any typed failure through the
// reporter. A null return means "do not admit to mempool"; the reported code is the stable
// LICENCE_* code.

using HushNode.Credentials;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushNode.HushVoting.Licence.Transactions;

public sealed class HushVotingLicenceContentHandler(
    ICredentialsProvider credentialProvider,
    IHushVotingLicenceTransactionValidator validator) : ITransactionContentHandler, IAsyncTransactionContentHandler, ITransactionValidationFailureReporter
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IHushVotingLicenceTransactionValidator _validator = validator;
    private TransactionValidationFailure? _pendingFailure;

    public bool CanValidate(Guid transactionKind) =>
        HushVotingLicenceAssignmentPayloadHandler.IsLicencePayloadKind(transactionKind);

    public AbstractTransaction? ValidateAndSign(AbstractTransaction transaction) =>
        ValidateAndSignAsync(transaction).GetAwaiter().GetResult();

    public async Task<AbstractTransaction?> ValidateAndSignAsync(AbstractTransaction transaction)
    {
        _pendingFailure = null;

        if (transaction is not SignedTransaction<HushVotingLicenceAssignmentPayload> licenceTransaction)
        {
            _pendingFailure = new TransactionValidationFailure(
                HushVotingLicenceValidationCodes.PayloadKindUnsupported,
                "Transaction kind does not match the licence payload.");
            return null;
        }

        var validation = await _validator.ValidateAsync(licenceTransaction, CancellationToken.None);
        if (!validation.IsValid)
        {
            // Expected validation failures are typed data (stable code), never exceptions.
            _pendingFailure = new TransactionValidationFailure(
                validation.ValidationCode ?? HushVotingLicenceValidationCodes.PayloadMalformed,
                validation.Message ?? "Licence transaction validation failed.");
            return null;
        }

        var blockProducerCredentials = _credentialProvider.GetCredentials();

        var signedByValidationTransaction = licenceTransaction.SignByValidator(
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
