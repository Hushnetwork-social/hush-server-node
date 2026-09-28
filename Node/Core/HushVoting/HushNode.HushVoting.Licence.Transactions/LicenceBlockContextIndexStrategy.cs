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
using HushShared.Identity.Model;
using Microsoft.EntityFrameworkCore;

namespace HushNode.HushVoting.Licence.Transactions;

public sealed class LicenceBlockContextIndexStrategy(
    IHushVotingLicenceTransactionValidator validator,
    Func<DbContext> contextFactory,
    LicenceCatalogueArchive archive,
    LicenceCacheOutboxPolicy? cacheOutbox = null) : IBlockContextIndexStrategy
{
    private readonly IHushVotingLicenceTransactionValidator _validator = validator;
    private readonly Func<DbContext> _contextFactory = contextFactory;
    private readonly LicenceCatalogueArchive _archive = archive;
    private readonly LicenceCacheOutboxPolicy? _cacheOutbox = cacheOutbox;

    public bool CanHandle(AbstractTransaction transaction) =>
        transaction.PayloadKind == HushVotingLicenceAssignmentPayloadHandler.LicenceAssignmentPayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction, BlockIndexContext blockContext)
    {
        if (transaction is not ValidatedTransaction<HushVotingLicenceAssignmentPayload> licenceTransaction)
        {
            throw new InvalidOperationException("Licence index strategy received an invalid transaction shape.");
        }

        // Replay verifies the signed bytes and identity, then resolves the exact approved
        // release observed by this transaction. Today's clock/state/release is irrelevant.
        var validation = await _validator.AuthenticateAsync(
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

        var configuration = _archive.Find(licenceTransaction.Payload.ObservedCatalogueVersion)
            ?? throw new InvalidOperationException("Licence block index requires its retained approved catalogue.");

        var identity = validation.ValidatedContent as HushVotingLicenceSignatoryContext
            ?? throw new InvalidOperationException("Licence signatory identity was not authenticated.");

        if (identity.IdentityCreationBlockIndex > blockContext.BlockIndex
            || blockContext.BlockCreationTimeUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException("Licence block index has incompatible historical context.");
        }

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

        var indexResult = await LicenceBlockIndexWriter.IndexAsync(
            _contextFactory,
            configuration,
            subject,
            licenceTransaction,
            blockContext.BlockIndex,
            blockContext.BlockCreationTimeUtc,
            _cacheOutbox,
            CancellationToken.None);

        // A licence transaction that is no longer valid at block time (stale/lower/same) or that
        // failed to write must never silently pass as indexed: deterministic replay requires a
        // visible, typed failure at the indexing boundary.
        if (!indexResult.Indexed)
        {
            throw new InvalidOperationException(
                $"Licence block index did not activate the transaction: {indexResult.StableErrorCode}");
        }
    }
}
