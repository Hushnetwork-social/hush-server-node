// FEAT-015 Task 3.5 — pure licence reservation-competition classifier.
//
// All admission/reservation decisions are deterministic functions of the durable row state and
// the incoming claim — extracted here so the DB store (HushVotingLicenceReservationStore) only
// loads rows and persists, while the decision logic is unit-testable without a container.
// Semantics (FeatureDescription "Admission, Idempotency, and Concurrency"):
//   - exact originating tx + exact fingerprint -> PENDING (matching existing);
//   - same originating tx + different fingerprint -> LICENCE_TRANSACTION_IDEMPOTENCY_MISMATCH;
//   - pending exists for another tx -> higher valid rank supersedes, equal rank is first-valid
//     (pending retained), lower rank rejected as LICENCE_TRANSITION_PENDING.

using HushNode.HushVoting.Licensing.Storage;

namespace HushNode.HushVoting.Licence.Transactions;

/// <summary>Read-model inputs for the competition classifier.</summary>
public sealed record HushVotingLicenceReservationRowState(
    bool HasSameOriginatingTransaction,
    string? ExistingFingerprint,
    bool HasPendingForSubject,
    int? PendingUpgradeRank);

public static class HushVotingLicenceReservationCompetition
{
    /// <summary>
    /// Pure decision given the current durable row state. Returns Accepted when the claim should
    /// be inserted (and the prior pending superseded when a higher valid rank wins), Pending on
    /// exact retry/first-valid same-rank, Rejected on idempotency mismatch or lower-rank
    /// competition. The caller persists the resulting transition.
    /// </summary>
    public static HushVotingLicenceReservationDecision Decide(
        HushVotingLicenceReservationRowState state,
        HushVotingLicenceReservationClaim claim)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(claim);

        if (state.HasSameOriginatingTransaction)
        {
            if (string.Equals(
                    state.ExistingFingerprint,
                    claim.CanonicalPayloadFingerprintSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return HushVotingLicenceReservationDecision.MatchPending();
            }

            return HushVotingLicenceReservationDecision.Reject(
                HushVotingLicenceValidationCodes.TransactionIdempotencyMismatch,
                "The transaction id was reused with different bytes.");
        }

        if (!state.HasPendingForSubject)
        {
            return HushVotingLicenceReservationDecision.Accept();
        }

        var pendingRank = state.PendingUpgradeRank ?? 0;
        if (claim.RequestedUpgradeRank < pendingRank)
        {
            return HushVotingLicenceReservationDecision.Reject(
                HushVotingLicenceValidationCodes.TransitionPending,
                "A higher-rank valid licence transition is already pending for this identity.");
        }

        if (claim.RequestedUpgradeRank == pendingRank)
        {
            // First-valid same-rank: the first valid claim keeps the reservation.
            return HushVotingLicenceReservationDecision.MatchPending(
                "A same-rank valid licence transition is already pending for this identity.");
        }

        // Higher valid rank supersedes the lower pending row.
        return HushVotingLicenceReservationDecision.AcceptSuperseding();
    }
}

/// <summary>Pure competition decision + which durable transition the caller must persist.</summary>
public sealed record HushVotingLicenceReservationDecision(
    HushVotingLicenceSubmitOutcome Outcome,
    string? ValidationCode,
    string? Message,
    bool ShouldInsert,
    bool ShouldSupersedeExistingPending)
{
    public static HushVotingLicenceReservationDecision Accept() =>
        new(HushVotingLicenceSubmitOutcome.Accepted, null, null, ShouldInsert: true, ShouldSupersedeExistingPending: false);

    public static HushVotingLicenceReservationDecision AcceptSuperseding() =>
        new(HushVotingLicenceSubmitOutcome.Accepted, null, "A higher valid rank superseded the pending transition.", ShouldInsert: true, ShouldSupersedeExistingPending: true);

    public static HushVotingLicenceReservationDecision MatchPending(string? message = null) =>
        new(HushVotingLicenceSubmitOutcome.Pending, null, message ?? "The exact licence transaction is already pending.", ShouldInsert: false, ShouldSupersedeExistingPending: false);

    public static HushVotingLicenceReservationDecision Reject(string code, string message) =>
        new(HushVotingLicenceSubmitOutcome.Rejected, code, message, ShouldInsert: false, ShouldSupersedeExistingPending: false);
}
