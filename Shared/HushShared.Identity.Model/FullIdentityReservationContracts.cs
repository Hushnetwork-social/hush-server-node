// FEAT-011 Task 2.7 — FullIdentity reservation, idempotency, and structured
// submit-outcome contracts (server side).
//
// Atomic admission is keyed by the exact signing identity/transaction:
//  - first valid same-key registration → ACCEPTED + one mempool item;
//  - exact retry/concurrent duplicate → PENDING, no second mempool item;
//  - indexed identity → ALREADY_EXISTS, no admission;
//  - conflicting same-signing pending → stable conflict result.
// The reservation lifecycle (AbsentUnreserved → ReservedPending → Indexed)
// and release/commit/restart semantics are frozen in
// transition-fault-matrix S1–S8. No generic signer, no message parsing, no
// human-attestation fields, no export capability.

namespace HushShared.Identity.Model;

/// <summary>Structured FullIdentity submission outcome (never free-form message control).</summary>
public enum FullIdentitySubmitOutcome
{
    Accepted,
    Pending,
    AlreadyExists,
    RejectedEditable,
    RejectedTerminal,
    Conflict,
    Unknown,
}

/// <summary>
/// Atomic reservation result keyed by the exact signing identity. Carries the
/// structured outcome and the stable validation code for rejections.
/// </summary>
public sealed record FullIdentityReservationResult(
    FullIdentitySubmitOutcome Outcome,
    string? ValidationCode)
{
    public static FullIdentityReservationResult Accepted() =>
        new(FullIdentitySubmitOutcome.Accepted, null);

    public static FullIdentityReservationResult Pending() =>
        new(FullIdentitySubmitOutcome.Pending, null);

    public static FullIdentityReservationResult AlreadyExists() =>
        new(FullIdentitySubmitOutcome.AlreadyExists, null);

    public static FullIdentityReservationResult Conflict() =>
        new(FullIdentitySubmitOutcome.Conflict, null);

    public static FullIdentityReservationResult Rejected(string validationCode) =>
        new(FullIdentitySubmitOutcome.RejectedTerminal, validationCode);

    public static FullIdentityReservationResult RejectedEditable(string validationCode) =>
        new(FullIdentitySubmitOutcome.RejectedEditable, validationCode);

    public static FullIdentityReservationResult Unknown() =>
        new(FullIdentitySubmitOutcome.Unknown, null);
}

/// <summary>
/// Identity-specific admission service contract. Atomically reserves one
/// signing identity/exact transaction before mempool insertion and converges
/// duplicates/concurrency deterministically. Implementations must be safe
/// under restart: a released reservation never leaves a phantom mempool item
/// and never blocks the exact same transaction from being re-admitted.
/// </summary>
public interface IFullIdentityReservationService
{
    /// <summary>
    /// Attempts atomic admission for the exact signing identity.
    /// Returns the structured outcome; never throws for expected outcomes.
    /// </summary>
    Task<FullIdentityReservationResult> ReserveAsync(
        string signingAddress,
        string transactionId,
        string transactionDigest,
        CancellationToken cancellationToken);

    /// <summary>Releases a reservation that can no longer be admitted (editable correction / terminal rejection).</summary>
    Task ReleaseAsync(string signingAddress, CancellationToken cancellationToken);

    /// <summary>Marks the identity indexed after commit (final state; never re-enters pending).</summary>
    Task MarkIndexedAsync(string signingAddress, CancellationToken cancellationToken);
}

/// <summary>
/// Reservation lifecycle vocabulary (mirrors transition-fault-matrix S1–S8).
/// Unknown states fail closed.
/// </summary>
public enum ReservationState
{
    AbsentUnreserved,
    ReservedPending,
    Indexed,
    Released,
}
