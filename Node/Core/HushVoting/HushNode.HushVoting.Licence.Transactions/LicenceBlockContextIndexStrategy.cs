// FEAT-015 Task 6.3 — licence block-context index strategy.
//
// The only index-time entry point that can activate a licence assignment. Registered through the
// additive IBlockContextIndexStrategy seam so the dispatcher supplies the containing block index +
// consensus timestamp. The strategy re-validates the transaction with the dependency-safe
// composite validator (signature/identity/catalogue/transition) and then writes the projection
// through LicenceBlockIndexWriter — never through a runtime service. The trusted subject is
// constructed through the storage boundary from the exact signatory resolved by the validator
// context source.

using HushNode.HushVoting.Licensing.Storage;
using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using Microsoft.EntityFrameworkCore;

namespace HushNode.HushVoting.Licence.Transactions;

public sealed class LicenceBlockContextIndexStrategy(
    IHushVotingLicenceTransactionValidator validator,
    IHushVotingLicenceValidationContextSource contextSource,
    Func<DbContext> contextFactory,
    LicenceServiceConfiguration configuration,
    LicenceCacheOutboxPolicy? cacheOutbox = null) : IBlockContextIndexStrategy
{
    private readonly IHushVotingLicenceTransactionValidator _validator = validator;
    private readonly IHushVotingLicenceValidationContextSource _contextSource = contextSource;
    private readonly Func<DbContext> _contextFactory = contextFactory;
    private readonly LicenceServiceConfiguration _configuration = configuration;
    private readonly LicenceCacheOutboxPolicy? _cacheOutbox = cacheOutbox;

    public bool CanHandle(AbstractTransaction transaction) =>
        transaction.PayloadKind == HushVotingLicenceAssignmentPayloadHandler.LicenceAssignmentPayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction, BlockIndexContext blockContext)
    {
        if (transaction is not ValidatedTransaction<HushVotingLicenceAssignmentPayload> licenceTransaction)
        {
            throw new InvalidOperationException("Licence index strategy received an invalid transaction shape.");
        }

        // The composite validator authenticates (signature -> identity -> catalogue -> state ->
        // transition) BEFORE indexing. Its ValidatedContent carries the server-owned facts; the
        // writer re-derives the authoritative decision at block time under the subject lock.
        var validation = await _validator.ValidateAsync(
            new SignedTransaction<HushVotingLicenceAssignmentPayload>(
                licenceTransaction,
                licenceTransaction.UserSignature),
            CancellationToken.None);

        if (!validation.IsValid)
        {
            // A block containing an invalid licence transaction must never be indexed into the
            // projection; deterministic block replay would otherwise diverge. Fail closed.
            throw new InvalidOperationException(
                $"Licence block index rejected an invalid transaction: {validation.ValidationCode}");
        }

        var signatory = HushVotingLicenceCanonicalAddress.Normalize(licenceTransaction.UserSignature.Signatory)
            ?? throw new InvalidOperationException("Licence signatory is not canonical.");

        var identity = await _contextSource.ResolveIdentityAsync(signatory, CancellationToken.None)
            ?? throw new InvalidOperationException("Licence signatory identity is not indexed.");

        if (!AuthenticatedIdentitySubject.TryCreate(
                LicencePersistenceVocabulary.SubjectTypeIdentity,
                identity.CanonicalPublicSigningAddress,
                identity.IdentityCreationBlockIndex,
                out var subject,
                out var stableError)
            || subject is null)
        {
            throw new InvalidOperationException(
                $"Licence signatory subject construction failed: {stableError}");
        }

        await LicenceBlockIndexWriter.IndexAsync(
            _contextFactory,
            _configuration,
            subject,
            licenceTransaction,
            blockContext.BlockIndex,
            blockContext.BlockCreationTimeUtc,
            _cacheOutbox,
            CancellationToken.None);
    }
}
