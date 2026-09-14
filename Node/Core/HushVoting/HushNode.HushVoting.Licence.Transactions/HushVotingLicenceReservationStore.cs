// FEAT-015 Task 3.5 — DB-backed licence pending-reservation store (decision D4).
//
// Loads the durable row state, classifies via the pure HushVotingLicenceReservationCompetition,
// and persists the resulting transition in one SaveChanges:
//   - exact retry -> PENDING (no new row);
//   - idempotency mismatch -> rejected;
//   - higher valid rank -> supersedes the pending row (append-only) + inserts the winner;
//   - equal/lower rank -> first-valid pending retained.
// Races converge on the partial-unique single-PENDING index and the unique originating-transaction
// index; a transient unique violation is treated as an exact-concurrent duplicate (PENDING) and
// never surfaces as an exception.

using HushNode.HushVoting.Licensing.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HushNode.HushVoting.Licence.Transactions;

public sealed class HushVotingLicenceReservationStore : IHushVotingLicenceReservationStore
{
    private readonly Func<DbContext> _contextFactory;

    public HushVotingLicenceReservationStore(Func<DbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<HushVotingLicenceAdmissionResult> ReserveAsync(
        HushVotingLicenceReservationClaim claim,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();

        using var db = _contextFactory();
        var reservations = db.Set<LicencePendingReservationEntity>();

        var sameTx = await reservations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.OriginatingTransactionId == claim.OriginatingTransactionId,
                cancellationToken)
            .ConfigureAwait(false);

        var pendingForSubject = await reservations
            .SingleOrDefaultAsync(
                r => r.LicenceSubjectId == claim.SubjectId
                     && r.LifecycleStatus == LicencePersistenceVocabulary.ReservationLifecyclePending,
                cancellationToken)
            .ConfigureAwait(false);

        var state = new HushVotingLicenceReservationRowState(
            HasSameOriginatingTransaction: sameTx is not null,
            ExistingFingerprint: sameTx?.CanonicalPayloadFingerprintSha256,
            HasPendingForSubject: pendingForSubject is not null,
            PendingUpgradeRank: pendingForSubject?.RequestedUpgradeRank);

        var decision = HushVotingLicenceReservationCompetition.Decide(state, claim);
        if (!decision.ShouldInsert)
        {
            return Map(decision);
        }

        if (decision.ShouldSupersedeExistingPending && pendingForSubject is not null)
        {
            pendingForSubject.LifecycleStatus = LicencePersistenceVocabulary.ReservationLifecycleSuperseded;
            pendingForSubject.ResolvedAtUtc = DateTime.UtcNow;
        }

        reservations.Add(ToEntity(claim));

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // A concurrent instance inserted the exact same transaction or reserved the identity
            // first. Under deterministic first-valid semantics the exact duplicate is PENDING.
            return HushVotingLicenceAdmissionResult.Pending(
                "A concurrent identical licence transaction was admitted first.");
        }

        return HushVotingLicenceAdmissionResult.Accepted();
    }

    public async Task<bool> ResolvePendingAsync(
        Guid subjectId,
        Guid originatingTransactionId,
        string lifecycleStatus,
        CancellationToken cancellationToken)
    {
        if (lifecycleStatus != LicencePersistenceVocabulary.ReservationLifecycleSuperseded
            && lifecycleStatus != LicencePersistenceVocabulary.ReservationLifecycleResolved)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifecycleStatus),
                "Resolution lifecycle must be superseded or resolved.");
        }

        using var db = _contextFactory();
        var pending = await db.Set<LicencePendingReservationEntity>()
            .SingleOrDefaultAsync(
                r => r.LicenceSubjectId == subjectId
                     && r.OriginatingTransactionId == originatingTransactionId
                     && r.LifecycleStatus == LicencePersistenceVocabulary.ReservationLifecyclePending,
                cancellationToken)
            .ConfigureAwait(false);

        if (pending is null)
        {
            return false;
        }

        pending.LifecycleStatus = lifecycleStatus;
        pending.ResolvedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static HushVotingLicenceAdmissionResult Map(HushVotingLicenceReservationDecision decision) =>
        decision.Outcome switch
        {
            HushVotingLicenceSubmitOutcome.Accepted => HushVotingLicenceAdmissionResult.Accepted(),
            HushVotingLicenceSubmitOutcome.Pending => HushVotingLicenceAdmissionResult.Pending(decision.Message),
            HushVotingLicenceSubmitOutcome.Rejected => HushVotingLicenceAdmissionResult.Rejected(
                decision.ValidationCode ?? HushVotingLicenceValidationCodes.TransitionPending,
                decision.Message ?? "Licence reservation rejected."),
            _ => HushVotingLicenceAdmissionResult.Unknown(),
        };

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres
            && postgres.SqlState == "23505";

    private static LicencePendingReservationEntity ToEntity(HushVotingLicenceReservationClaim claim) =>
        new()
        {
            LicencePendingReservationId = Guid.CreateVersion7(),
            LicenceSubjectId = claim.SubjectId,
            OriginatingTransactionId = claim.OriginatingTransactionId,
            CanonicalPayloadFingerprintSha256 = claim.CanonicalPayloadFingerprintSha256,
            TransitionIntent = claim.TransitionIntent,
            RequestedPlanId = claim.RequestedPlanId,
            ObservedCatalogueVersion = claim.ObservedCatalogueVersion,
            ExpectedCurrentLicenceTransactionId = claim.ExpectedCurrentLicenceTransactionId,
            ExpectedCurrentPlanId = claim.ExpectedCurrentPlanId,
            LifecycleStatus = LicencePersistenceVocabulary.ReservationLifecyclePending,
            RequestedUpgradeRank = claim.RequestedUpgradeRank,
            CreatedAtUtc = DateTime.UtcNow,
            ResolvedAtUtc = null,
        };
}
