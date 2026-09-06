// FEAT-015 Task 6.3 — licence block-index writer (the only activation point).
//
// Indexes a validated licence assignment transaction into PostgreSQL deterministically using the
// containing block's index + consensus timestamp as authoritative provenance. Everything happens
// in ONE local database transaction and SaveChanges: subject upsert, supersede of the previously
// active assignment when an upgrade indexes, the new assignment row with origin provenance,
// transition events, the subject revision increment (exactly once), and — when the FEAT-014
// cache-outbox policy is enabled — one outbox row. Mempool acceptance alone never reaches this
// writer; the block index dispatcher is the sole caller.
//
// Replay determinism: the originating-transaction unique index makes a duplicated index attempt
// converge (no second row, no revision bump). This writer is NOT a runtime service: no gRPC,
// timer, expiry handler, or direct-call path reaches it outside block indexing (architecture
// guard T1/T2a).

using HushNode.HushVoting.Licensing.Storage;
using HushShared.Blockchain.TransactionModel.States;
using Microsoft.EntityFrameworkCore;
using HushShared.HushVoting.Licensing.Model;

namespace HushNode.HushVoting.Licence.Transactions;

/// <summary>Typed outcome of a licence block-index attempt. Expected states are data, never exceptions.</summary>
public sealed record LicenceBlockIndexResult(
    bool Indexed,
    Guid LicenceSubjectId,
    Guid LicenceAssignmentId,
    long EntitlementRevision,
    string? StableErrorCode,
    string? SafeReason)
{
    public static LicenceBlockIndexResult Ok(
        Guid subjectId,
        Guid assignmentId,
        long revision) =>
        new(true, subjectId, assignmentId, revision, null, null);

    public static LicenceBlockIndexResult ConvergedDuplicate(Guid subjectId) =>
        new(true, subjectId, Guid.Empty, 0, null, "Originating transaction already indexed.");

    public static LicenceBlockIndexResult Fail(string stableErrorCode, string safeReason) =>
        new(false, Guid.Empty, Guid.Empty, 0, stableErrorCode, safeReason);
}

/// <summary>
/// Applies one validated licence assignment transaction from a finalized block. The caller has
/// already verified signature, identity, catalogue, and transition semantics (composite
/// validator) and supplies the resulting operative facts.
/// </summary>
public static class LicenceBlockIndexWriter
{
    public static async Task<LicenceBlockIndexResult> IndexAsync(
        Func<DbContext> contextFactory,
        LicenceServiceConfiguration configuration,
        HushNode.HushVoting.Licensing.Storage.AuthenticatedIdentitySubject subject,
        ValidatedTransaction<HushVotingLicenceAssignmentPayload> transaction,
        long blockIndex,
        DateTime blockCreationTimeUtc,
        LicenceCacheOutboxPolicy? cacheOutbox,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(transaction);
        // The authoritative decision is derived at block time under the subject lock; nothing on
        // entry is trusted beyond the already-validated transaction and server configuration.

        await using var db = contextFactory();
        try
        {
            await using var dbTransaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var subjectRow = await LockOrCreateSubjectAsync(db, subject, blockCreationTimeUtc, cancellationToken);

            // Replay/idempotency: the originating transaction UUID must not already be indexed.
            var alreadyIndexed = await db.Set<LicenceAssignmentEntity>()
                .AsNoTracking()
                .AnyAsync(
                    a => a.OriginatingTransactionId == transaction.TransactionId.Value,
                    cancellationToken);
            if (alreadyIndexed)
            {
                await dbTransaction.CommitAsync(cancellationToken);
                return LicenceBlockIndexResult.ConvergedDuplicate(subjectRow.LicenceSubjectId);
            }

            var previousActive = await db.Set<LicenceAssignmentEntity>()
                .SingleOrDefaultAsync(
                    a => a.LicenceSubjectId == subjectRow.LicenceSubjectId
                         && a.LifecycleStatus == LicencePersistenceVocabulary.LifecycleActive,
                    cancellationToken);

            // Deterministic block validation: derive the authoritative decision against the LOCKED
            // indexed state at the containing-block instant. The pre-mempool decision is advisory
            // only; concurrent valid higher transitions may have indexed first, so stale/lower/same
            // transitions must never activate (at-most-one effective assignment).
            var authoritative = HushVotingLicenceTransitionDecisionCore.Decide(
                configuration.Catalogue,
                transaction.Payload,
                LicenceBlockIndexWriterDecisions.CurrentlyActiveState(configuration.Catalogue, previousActive));
            if (!authoritative.IsValid || authoritative.OperativeFacts is null)
            {
                // A stale/lower/same transition that is no longer valid at block time is never
                // indexed; the previously active assignment (if any) stays authoritative.
                await dbTransaction.CommitAsync(cancellationToken);
                return LicenceBlockIndexResult.Fail(
                    authoritative.ValidationCode ?? HushVotingLicenceValidationCodes.TransitionNotHigher,
                    authoritative.Message ?? "The licence transition is no longer valid at block time.");
            }

            var facts = authoritative.OperativeFacts;
            var assignmentId = Guid.CreateVersion7();
            if (previousActive is not null)
            {
                // Lifecycle flip first; the SupersededByAssignmentId pointer is attached in a second
                // SaveChanges after the new active row exists (self-FK ordering, FEAT-013 pattern).
                previousActive.LifecycleStatus = LicencePersistenceVocabulary.LifecycleSuperseded;
                previousActive.LifecycleChangedAtUtc = blockCreationTimeUtc;
                previousActive.LifecycleReason = LicenceEntitlementDecisions.ReasonSupersededByAutomaticUpgrade;
            }

            var expiresAtUtc = facts.Term.IsPerpetual
                ? null
                : LicenceEntitlementDecisions.ComputeExpiryInstant(blockCreationTimeUtc, facts.Term);
            var revision = subjectRow.EntitlementRevision + 1;
            var source = string.Equals(
                facts.TransitionIntent,
                HushVotingLicenceTransitionIntent.BaselineFree,
                StringComparison.Ordinal)
                ? LicencePersistenceVocabulary.SourceBaselineFree
                : LicencePersistenceVocabulary.SourceConfirmedUpgrade;

            var assignment = new LicenceAssignmentEntity
            {
                LicenceAssignmentId = assignmentId,
                LicenceSubjectId = subjectRow.LicenceSubjectId,
                PlanId = facts.PlanId.Value,
                AssignedCatalogueVersion = configuration.CatalogueVersion,
                AssignedCatalogueDigestSha256 = configuration.ReleaseDigestSha256,
                LifecycleStatus = LicencePersistenceVocabulary.LifecycleActive,
                Source = source,
                EffectiveFromUtc = blockCreationTimeUtc,
                ExpiresAtUtc = expiresAtUtc,
                PlanFamily = facts.PlanFamily,
                UpgradeRank = facts.UpgradeRank,
                EligibleVoterCap = facts.EligibleVoterCap,
                UnlimitedElectionPolicy = facts.UnlimitedElections,
                TermKind = facts.TermKind,
                TermYears = facts.TermYears,
                AllowedGovernanceOptionIds = facts.GovernanceOptionIds.ToArray(),
                OriginatingTransactionId = transaction.TransactionId.Value,
                OriginatingBlockIndex = blockIndex,
                OriginatingBlockTimeStampUtc = blockCreationTimeUtc,
            };
            db.Set<LicenceAssignmentEntity>().Add(assignment);

            subjectRow.EntitlementRevision = revision;

            // Append-only transition evidence (created; superseded when an upgrade indexes). The
            // per-subject sequence continues from the existing max so replay never collides.
            var existingMaxSequence = await db.Set<LicenceTransitionEventEntity>()
                .Where(e => e.LicenceSubjectId == subjectRow.LicenceSubjectId)
                .MaxAsync(e => (long?)e.EventSequence, cancellationToken) ?? 0L;

            if (previousActive is not null)
            {
                db.Set<LicenceTransitionEventEntity>().Add(ToEvent(
                    subjectRow.LicenceSubjectId,
                    existingMaxSequence + 1,
                    LicencePersistenceVocabulary.EventTypeSuperseded,
                    revision,
                    previousActive.LicenceAssignmentId,
                    previousActive.PlanId,
                    previousActive.AssignedCatalogueVersion,
                    LicenceEntitlementDecisions.ReasonSupersededByAutomaticUpgrade,
                    blockCreationTimeUtc));
            }

            db.Set<LicenceTransitionEventEntity>().Add(ToEvent(
                subjectRow.LicenceSubjectId,
                existingMaxSequence + (previousActive is null ? 1L : 2L),
                LicencePersistenceVocabulary.EventTypeCreated,
                revision,
                assignmentId,
                facts.PlanId.Value,
                configuration.CatalogueVersion,
                source,
                blockCreationTimeUtc));

            AddCacheOutboxRowIfEnabled(
                db,
                cacheOutbox,
                subjectRow.LicenceSubjectId,
                revision,
                LicenceCacheOutboxChangeKinds.ActivatedHigherPlan,
                blockCreationTimeUtc);

            await db.SaveChangesAsync(cancellationToken);

            if (previousActive is not null)
            {
                previousActive.SupersededByAssignmentId = assignmentId;
                await db.SaveChangesAsync(cancellationToken);
            }

            await dbTransaction.CommitAsync(cancellationToken);

            return LicenceBlockIndexResult.Ok(subjectRow.LicenceSubjectId, assignmentId, revision);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The originating-transaction unique index converges duplicate index attempts; any
            // other failure fails closed (nothing half-applied after rollback).
            return LicenceBlockIndexResult.Fail(
                "licence_index_write_failed",
                "The licence block index write did not commit.");
        }
    }

    private static LicenceTransitionEventEntity ToEvent(
        Guid subjectId,
        long sequence,
        string eventType,
        long revision,
        Guid assignmentId,
        string planId,
        string catalogueDecisionVersion,
        string? sourceOrReason,
        DateTime occurredAtUtc) =>
        new()
        {
            LicenceTransitionEventId = Guid.CreateVersion7(),
            LicenceSubjectId = subjectId,
            EventSequence = sequence,
            EventType = eventType,
            SubjectRevision = revision,
            AssignmentId = assignmentId,
            PlanId = planId,
            CatalogueDecisionVersion = catalogueDecisionVersion,
            SourceOrReason = sourceOrReason,
            OperationReferenceId = null,
            OccurredAtUtc = occurredAtUtc,
        };

    private static async Task<LicenceSubjectEntity> LockOrCreateSubjectAsync(
        DbContext db,
        HushNode.HushVoting.Licensing.Storage.AuthenticatedIdentitySubject subject,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var existing = await db.Set<LicenceSubjectEntity>()
            .FromSqlRaw(
                """
                SELECT * FROM "HushVoting"."LicenceSubject"
                WHERE "SubjectType" = {0} AND "CanonicalPublicSigningAddress" = {1} FOR UPDATE
                """,
                subject.SubjectType,
                subject.CanonicalPublicSigningAddress)
            .SingleOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var created = new LicenceSubjectEntity
        {
            LicenceSubjectId = Guid.CreateVersion7(),
            SubjectType = subject.SubjectType,
            CanonicalPublicSigningAddress = subject.CanonicalPublicSigningAddress,
            IdentityCreationBlockIndex = subject.IdentityCreationBlockIndex,
            CreatedAtUtc = nowUtc,
            EntitlementRevision = 0,
        };

        db.Add(created);
        await db.SaveChangesAsync(cancellationToken);
        return created;
    }

    /// <summary>Adds one FEAT-014 outbox row inside the same transaction when the policy is enabled.</summary>
    private static void AddCacheOutboxRowIfEnabled(
        DbContext db,
        LicenceCacheOutboxPolicy? policy,
        Guid licenceSubjectId,
        long committedRevision,
        string changeKind,
        DateTime nowUtc)
    {
        if (policy is not { Enabled: true })
        {
            return;
        }

        if (!LicenceCacheOutboxChangeKinds.TryValidate(changeKind, out var errorCode))
        {
            throw new InvalidOperationException("Cache outbox change kind invalid: " + errorCode);
        }

        db.Set<LicenceCacheOutboxEntity>().Add(new LicenceCacheOutboxEntity
        {
            Id = Guid.CreateVersion7(),
            LicenceSubjectId = licenceSubjectId,
            CommittedRevision = committedRevision,
            ChangeKind = changeKind,
            CreatedUtc = nowUtc,
            AvailableAfterUtc = nowUtc,
            AttemptCount = 0,
            LeaseOwnerToken = null,
            LeaseExpiresUtc = null,
            DeliveredUtc = null,
            LastSafeErrorCode = null,
            LastAttemptUtc = null,
        });
    }
}
