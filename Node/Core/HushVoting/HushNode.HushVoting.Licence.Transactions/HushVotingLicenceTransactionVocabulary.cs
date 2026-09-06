// FEAT-015 Task 2.3 — licence transaction payload and stable vocabulary (contract layer).
//
// Freezes the sole closed `HushVotingLicenceAssignmentPayload` transaction kind:
//  - kind GUID 71370664-5eb4-4ce9-b96a-d7e7ffe53db5 (immutable; AC-015-007);
//  - two closed intents (baseline_free | confirmed_upgrade);
//  - bounded, client-authorable fields only (no identity, subject, licence id,
//    address, date, cap, governance, rank, lifecycle, source, payment, or
//    server-decision member);
//  - the outer transaction TransactionId IS the public licence reference (there is
//    no second LicenceId on the payload);
//  - unknown versions/extensions/intents/codes fail closed (never coerced).
//
// Canonical JSON member names and declaration order are frozen here (the Phase 2.4
// fixture artifact locks the byte vectors; the Phase 3.1 codec reproduces them).

namespace HushNode.HushVoting.Licence.Transactions;

/// <summary>
/// Closed v1 licence assignment transition intents. Wire values are ordinal,
/// culture-independent strings; unknown values are rejected, never defaulted.
/// </summary>
public static class HushVotingLicenceTransitionIntent
{
    /// <summary>Client-signed baseline when no active entitlement exists (Direct Free).</summary>
    public const string BaselineFree = "baseline_free";

    /// <summary>Client-confirmed upgrade to a strictly higher available Veritas plan.</summary>
    public const string ConfirmedUpgrade = "confirmed_upgrade";

    /// <summary>The exact closed set of v1 intents (nothing else is valid).</summary>
    public static readonly IReadOnlyList<string> Known = new[] { BaselineFree, ConfirmedUpgrade };

    /// <summary>Parses an intent ordinally; null when unknown or malformed (fail closed).</summary>
    public static bool TryParse(string? value, out string intent)
    {
        intent = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed != BaselineFree && trimmed != ConfirmedUpgrade)
        {
            return false;
        }

        intent = trimmed;
        return true;
    }
}

/// <summary>
/// Closed field bounds for the licence assignment payload. Values mirror the
/// projection/column bounds (plan id varchar(64), catalogue version varchar(96))
/// and the FEAT-012 catalogue release schema so any structurally valid payload is
/// indexable without truncation.
/// </summary>
public static class HushVotingLicencePayloadBounds
{
    /// <summary>Maximum UTF-8 byte length of a plan id on the payload (schema varchar(64)).</summary>
    public const int MaxPlanIdUtf8Bytes = 64;

    /// <summary>Maximum UTF-8 byte length of the observed catalogue version (schema varchar(96)).</summary>
    public const int MaxCatalogueVersionUtf8Bytes = 96;
}

/// <summary>
/// Canonical licence payload JSON member names and their frozen declaration order.
/// The Phase 3.1 canonical serializer and the Phase 2.4 byte fixtures must agree
/// with this exact order; never reorder or rename a member.
/// </summary>
public static class HushVotingLicencePayloadCanonicalMembers
{
    public const string TransitionIntent = "TransitionIntent";
    public const string RequestedPlanId = "RequestedPlanId";
    public const string ObservedCatalogueVersion = "ObservedCatalogueVersion";
    public const string ExpectedCurrentLicenceTransactionId = "ExpectedCurrentLicenceTransactionId";
    public const string ExpectedCurrentPlanId = "ExpectedCurrentPlanId";

    /// <summary>Frozen declaration order of canonical payload JSON members.</summary>
    public static readonly IReadOnlyList<string> Order = new[]
    {
        TransitionIntent,
        RequestedPlanId,
        ObservedCatalogueVersion,
        ExpectedCurrentLicenceTransactionId,
        ExpectedCurrentPlanId,
    };

    /// <summary>Baseline serialization omits the expected-current precondition pair entirely.</summary>
    public static readonly IReadOnlyList<string> BaselineMembers = new[]
    {
        TransitionIntent,
        RequestedPlanId,
        ObservedCatalogueVersion,
    };

    /// <summary>Upgrade serialization includes the full closed member set.</summary>
    public static readonly IReadOnlyList<string> UpgradeMembers = Order;
}
