// FEAT-015 Task 2.3 — the sole closed licence assignment payload.
//
// One canonical payload kind for the whole feature: `HushVotingLicenceAssignmentPayload`
// (kind GUID 71370664-5eb4-4ce9-b96a-d7e7ffe53db5). The payload is a versioned closed
// shape with exactly five client-authorable members:
//
//   TransitionIntent                     baseline_free | confirmed_upgrade (closed)
//   RequestedPlanId                      bounded FEAT-012 stable plan id
//   ObservedCatalogueVersion             bounded immutable catalogue release
//   ExpectedCurrentLicenceTransactionId  absent for baseline; canonical UUID for upgrade
//   ExpectedCurrentPlanId                absent for baseline; exact plan id for upgrade
//
// The standard outer transaction carries TransactionId, PayloadKind, transaction
// timestamp, payload size, and the real user signature. The outer TransactionId IS the
// public licence reference — there is no second LicenceId on the payload.
//
// The payload intentionally contains no effective/expiry date, cap, election count,
// governance choice, rank, lifecycle, source, revision, identity address, price,
// payment placeholder, Enterprise request, or server decision. Unknown intent values
// fail closed at construction and never coerce to a known intent.

using HushShared.Blockchain.TransactionModel;

namespace HushNode.HushVoting.Licence.Transactions;

/// <summary>
/// Closed licence assignment payload. Canonical JSON member names/order equal the C#
/// property names/declaration order (see <see cref="HushVotingLicencePayloadCanonicalMembers"/>).
/// The expected-current precondition pair is null/absent for <see cref="HushVotingLicenceTransitionIntent.BaselineFree"/>
/// and required for <see cref="HushVotingLicenceTransitionIntent.ConfirmedUpgrade"/>.
/// </summary>
public sealed record HushVotingLicenceAssignmentPayload(
    string TransitionIntent,
    string RequestedPlanId,
    string ObservedCatalogueVersion,
    Guid? ExpectedCurrentLicenceTransactionId = null,
    string? ExpectedCurrentPlanId = null) : ITransactionPayloadKind;

/// <summary>Static licence-payload contract: the sole kind GUID and creation surface.</summary>
public static class HushVotingLicenceAssignmentPayloadHandler
{
    /// <summary>The one licence payload kind (frozen; AC-015-007).</summary>
    public static Guid LicenceAssignmentPayloadKind { get; } = Guid.Parse("71370664-5eb4-4ce9-b96a-d7e7ffe53db5");

    /// <summary>True for the exact licence payload kind; any other kind fails closed.</summary>
    public static bool IsLicencePayloadKind(Guid payloadKind) =>
        LicenceAssignmentPayloadKind == payloadKind;
}
