// FEAT-015 Task 3.5 — licence admission, reservation, and exact-idempotency contracts.
//
// Mirrors FullIdentityReservationContracts + FullIdentityAdmissionService outcomes but is
// DB-backed (decision D4) so two instances/identities converge deterministically. Admission
// order: composite validation -> indexed check -> atomic per-identity reservation keyed by the
// exact signed transaction UUID + canonical fingerprint. Exact retry returns PENDING (never a
// second mempool item); reused transaction id with different bytes is an idempotency mismatch;
// one PENDING reservation exists per identity; a lower-rank pending cannot replace a higher-rank
// valid pending; same-rank competition is first-valid.

using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushNode.HushVoting.Licence.Transactions;

/// <summary>Structured licence submission/admission outcome (never free-form control).</summary>
public enum HushVotingLicenceSubmitOutcome
{
    Accepted,
    Pending,
    AlreadyExists,
    Rejected,
    Unknown,
}

/// <summary>Atomic licence admission result. Expected outcomes are data, never exceptions.</summary>
public sealed record HushVotingLicenceAdmissionResult(
    HushVotingLicenceSubmitOutcome Outcome,
    string? ValidationCode,
    string? Message)
{
    public static HushVotingLicenceAdmissionResult Accepted() =>
        new(HushVotingLicenceSubmitOutcome.Accepted, null, null);

    public static HushVotingLicenceAdmissionResult Pending(string? message = null) =>
        new(HushVotingLicenceSubmitOutcome.Pending, null, message ?? "The exact licence transaction is already pending.");

    public static HushVotingLicenceAdmissionResult AlreadyExists(string? message = null) =>
        new(HushVotingLicenceSubmitOutcome.AlreadyExists, null, message ?? "The licence transaction is already indexed.");

    public static HushVotingLicenceAdmissionResult Rejected(string validationCode, string message) =>
        new(HushVotingLicenceSubmitOutcome.Rejected, validationCode, message);

    public static HushVotingLicenceAdmissionResult Unknown(string? message = null) =>
        new(HushVotingLicenceSubmitOutcome.Unknown, null, message ?? "Licence admission outcome is unknown.");
}

/// <summary>Reservation claim for the DB-backed admission store.</summary>
public sealed record HushVotingLicenceReservationClaim(
    Guid SubjectId,
    Guid OriginatingTransactionId,
    string CanonicalPayloadFingerprintSha256,
    string TransitionIntent,
    string RequestedPlanId,
    string ObservedCatalogueVersion,
    Guid? ExpectedCurrentLicenceTransactionId,
    string? ExpectedCurrentPlanId,
    int RequestedUpgradeRank);

/// <summary>
/// DB-backed per-identity pending-reservation store contract (one pending reservation per
/// identity; unique originating transaction). Implementations are safe under restart and
/// concurrent instances.
/// </summary>
public interface IHushVotingLicenceReservationStore
{
    /// <summary>Atomic reservation attempt keyed by the exact signed transaction UUID + fingerprint.</summary>
    Task<HushVotingLicenceAdmissionResult> ReserveAsync(
        HushVotingLicenceReservationClaim claim,
        CancellationToken cancellationToken);

    /// <summary>Resolves a pending reservation (indexed/rejected); returns false when it was not pending.</summary>
    Task<bool> ResolvePendingAsync(
        Guid subjectId,
        Guid originatingTransactionId,
        string lifecycleStatus,
        CancellationToken cancellationToken);
}
