// FEAT-015 Task 6.3 — licence admission service (validate -> indexed check -> DB reservation).
//
// Orchestrates the pre-mempool licence admission contract, mirroring the FullIdentity admission
// gate but with the DB-backed per-identity reservation (D4):
//   1. composite validation (kind -> shape -> size -> real signature -> identity -> catalogue ->
//      current state -> transition decision), typed outcomes only;
//   2. indexed-truth check: an already-indexed originating transaction -> ALREADY_EXISTS;
//   3. atomic DB reservation keyed by exact transaction UUID + canonical fingerprint:
//      first valid -> ACCEPTED; exact retry -> PENDING; idempotency mismatch / lower-rank
//      competition -> Rejected with the stable code.
// Expected outcomes are data, never exceptions.

using HushNode.HushVoting.Licensing.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HushNode.HushVoting.Licence.Transactions;

public sealed class HushVotingLicenceAdmissionService(
    IHushVotingLicenceTransactionValidator validator,
    IHushVotingLicenceValidationContextSource contextSource,
    IHushVotingLicenceReservationStore reservationStore,
    Func<DbContext> contextFactory) : IHushVotingLicenceAdmissionGate
{
    private readonly IHushVotingLicenceTransactionValidator _validator = validator;
    private readonly IHushVotingLicenceValidationContextSource _contextSource = contextSource;
    private readonly IHushVotingLicenceReservationStore _reservationStore = reservationStore;
    private readonly Func<DbContext> _contextFactory = contextFactory;

    public async Task<HushVotingLicenceAdmissionResult> AdmitAsync(
        AbstractTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not SignedTransaction<HushVotingLicenceAssignmentPayload> licenceTransaction)
        {
            return HushVotingLicenceAdmissionResult.Rejected(
                HushVotingLicenceValidationCodes.PayloadKindUnsupported,
                "Transaction is not a signed licence assignment.");
        }

        return await AdmitAsync(licenceTransaction, cancellationToken);
    }

    private async Task<HushVotingLicenceAdmissionResult> AdmitAsync(
        SignedTransaction<HushVotingLicenceAssignmentPayload> transaction,
        CancellationToken cancellationToken)
    {
        // 1. Composite validation (real signature before identity/state semantics).
        var validation = await _validator.ValidateAsync(transaction, cancellationToken);
        if (!validation.IsValid)
        {
            return HushVotingLicenceAdmissionResult.Rejected(
                validation.ValidationCode ?? HushVotingLicenceValidationCodes.PayloadMalformed,
                validation.Message ?? "Licence transaction validation failed.");
        }

        if (validation.ValidatedContent is not HushVotingLicenceTransitionDecision decision
            || decision.OperativeFacts is null)
        {
            return HushVotingLicenceAdmissionResult.Rejected(
                HushVotingLicenceValidationCodes.TransitionNotHigher,
                "Licence transition decision did not produce operative facts.");
        }

        // 2. Indexed-truth check: already-indexed originating transaction -> ALREADY_EXISTS.
        var alreadyIndexed = await IsIndexedAsync(transaction.TransactionId.Value, cancellationToken);
        if (alreadyIndexed)
        {
            return HushVotingLicenceAdmissionResult.AlreadyExists();
        }

        // 3. Atomic DB reservation (per-identity; exact uuid + fingerprint).
        var signatory = HushVotingLicenceCanonicalAddress.Normalize(transaction.UserSignature.Signatory)
            ?? throw new InvalidOperationException("Licence signatory is not canonical.");
        var identity = await _contextSource.ResolveIdentityAsync(signatory, cancellationToken);
        if (identity is null)
        {
            return HushVotingLicenceAdmissionResult.Rejected(
                HushVotingLicenceValidationCodes.SignatoryIdentityNotFound,
                "The signatory does not own an exact indexed HushNetwork identity.");
        }

        var subjectId = await ResolveSubjectIdAsync(identity, cancellationToken);
        var claim = new HushVotingLicenceReservationClaim(
            subjectId,
            transaction.TransactionId.Value,
            ComputeFingerprint(transaction),
            transaction.Payload.TransitionIntent,
            decision.OperativeFacts.PlanId.Value,
            transaction.Payload.ObservedCatalogueVersion,
            transaction.Payload.ExpectedCurrentLicenceTransactionId,
            transaction.Payload.ExpectedCurrentPlanId,
            decision.OperativeFacts.UpgradeRank);

        return await _reservationStore.ReserveAsync(claim, cancellationToken);
    }

    private async Task<bool> IsIndexedAsync(Guid originatingTransactionId, CancellationToken cancellationToken)
    {
        using var db = _contextFactory();
        return await db.Set<LicenceAssignmentEntity>()
            .AsNoTracking()
            .AnyAsync(a => a.OriginatingTransactionId == originatingTransactionId, cancellationToken);
    }

    private async Task<Guid> ResolveSubjectIdAsync(
        HushVotingLicenceSignatoryContext identity,
        CancellationToken cancellationToken)
    {
        using var db = _contextFactory();

        // The subject row is the durable identity anchor (no licence authority). Admission creates
        // it when the identity has never had a licence so the per-identity reservation can FK to it;
        // the block-index writer later finds the same row by canonical address. Creation is
        // idempotent on the unique (type, address) index; a concurrent create simply converges.
        var row = await db.Set<LicenceSubjectEntity>()
            .SingleOrDefaultAsync(
                s => s.SubjectType == LicencePersistenceVocabulary.SubjectTypeIdentity
                     && s.CanonicalPublicSigningAddress == identity.CanonicalPublicSigningAddress,
                cancellationToken);

        if (row is not null)
        {
            return row.LicenceSubjectId;
        }

        var created = new LicenceSubjectEntity
        {
            LicenceSubjectId = Guid.CreateVersion7(),
            SubjectType = LicencePersistenceVocabulary.SubjectTypeIdentity,
            CanonicalPublicSigningAddress = identity.CanonicalPublicSigningAddress,
            IdentityCreationBlockIndex = identity.IdentityCreationBlockIndex,
            CreatedAtUtc = DateTime.UtcNow,
            EntitlementRevision = 0,
        };
        db.Set<LicenceSubjectEntity>().Add(created);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException exception)
            when (exception.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Concurrent admission created the anchor first; reload it.
            var existing = await db.Set<LicenceSubjectEntity>()
                .AsNoTracking()
                .SingleAsync(
                    s => s.SubjectType == LicencePersistenceVocabulary.SubjectTypeIdentity
                         && s.CanonicalPublicSigningAddress == identity.CanonicalPublicSigningAddress,
                    cancellationToken);
            return existing.LicenceSubjectId;
        }

        return created.LicenceSubjectId;
    }

    /// <summary>Stable digest over the exact canonical unsigned JSON (sha-256 hex, lowercase).</summary>
    public static string ComputeFingerprint(SignedTransaction<HushVotingLicenceAssignmentPayload> transaction)
    {
        var canonicalJson = new HushVotingLicenceCanonicalSerializer().SerializeCanonicalUnsignedJson(transaction);
        return Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();
    }
}

/// <summary>Licence admission gate consumed by the transaction ingress.</summary>
public interface IHushVotingLicenceAdmissionGate
{
    Task<HushVotingLicenceAdmissionResult> AdmitAsync(
        AbstractTransaction transaction,
        CancellationToken cancellationToken);
}
