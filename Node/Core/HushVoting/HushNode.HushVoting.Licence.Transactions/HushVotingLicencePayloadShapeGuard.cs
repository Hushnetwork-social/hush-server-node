// FEAT-015 Task 2.3 — structural licence payload shape guard.
//
// Data-layer contract for the closed payload shape. This guard is authoritative for
// the *shape* of a payload only: closed intents, bounded plan/catalogue values,
// baseline absence vs upgrade presence of the expected-current precondition pair,
// and canonical UUID form. Business/catalogue/chain-state semantics (transition
// matrix, availability, staleness, rank, identity) are intentionally out of scope
// here and are evaluated by the Phase 3 licence validator against server-owned
// catalogue and indexed state.

using System.Globalization;

namespace HushNode.HushVoting.Licence.Transactions;

/// <summary>Typed structural outcome for payload-shape checks. Expected failures are data, never exceptions.</summary>
public sealed record HushVotingLicencePayloadShapeResult(
    bool IsValid,
    string? ValidationCode,
    string? Message,
    HushVotingLicenceAssignmentPayload? Payload = null)
{
    public static HushVotingLicencePayloadShapeResult Valid(HushVotingLicenceAssignmentPayload payload) =>
        new(true, null, null, payload);

    public static HushVotingLicencePayloadShapeResult Invalid(string code, string message) =>
        new(false, code, message, null);
}

/// <summary>
/// Closed-shape validation for <see cref="HushVotingLicenceAssignmentPayload"/>. Unknown
/// intents, over-bound plan/catalogue values, baseline preconditions, missing upgrade
/// preconditions, and malformed canonical UUIDs fail closed with stable codes. This guard
/// never consults the catalogue or the chain; it only locks the payload contract.
/// </summary>
public static class HushVotingLicencePayloadShapeGuard
{
    public static HushVotingLicencePayloadShapeResult Validate(
        HushVotingLicenceAssignmentPayload? payload)
    {
        if (payload is null)
        {
            return HushVotingLicencePayloadShapeResult.Invalid(
                HushVotingLicenceValidationCodes.PayloadMalformed,
                "Licence assignment payload is null.");
        }

        if (!HushVotingLicenceTransitionIntent.TryParse(payload.TransitionIntent, out var intent))
        {
            return HushVotingLicencePayloadShapeResult.Invalid(
                HushVotingLicenceValidationCodes.IntentUnknown,
                "Licence assignment transition intent is unknown.");
        }

        if (!IsBoundedNonEmpty(payload.RequestedPlanId, HushVotingLicencePayloadBounds.MaxPlanIdUtf8Bytes))
        {
            return HushVotingLicencePayloadShapeResult.Invalid(
                HushVotingLicenceValidationCodes.PayloadMalformed,
                "Requested plan id is empty or exceeds the bounded plan id length.");
        }

        if (!IsBoundedNonEmpty(
                payload.ObservedCatalogueVersion,
                HushVotingLicencePayloadBounds.MaxCatalogueVersionUtf8Bytes))
        {
            return HushVotingLicencePayloadShapeResult.Invalid(
                HushVotingLicenceValidationCodes.PayloadMalformed,
                "Observed catalogue version is empty or exceeds the bounded catalogue version length.");
        }

        return intent switch
        {
            HushVotingLicenceTransitionIntent.BaselineFree => ValidateBaseline(payload),
            HushVotingLicenceTransitionIntent.ConfirmedUpgrade => ValidateUpgrade(payload),
            _ => HushVotingLicencePayloadShapeResult.Invalid(
                HushVotingLicenceValidationCodes.IntentUnknown,
                "Licence assignment transition intent is unknown."),
        };
    }

    private static HushVotingLicencePayloadShapeResult ValidateBaseline(HushVotingLicenceAssignmentPayload payload)
    {
        // Baseline must carry NO expected-current precondition pair.
        if (payload.ExpectedCurrentLicenceTransactionId is not null
            || payload.ExpectedCurrentPlanId is not null)
        {
            return HushVotingLicencePayloadShapeResult.Invalid(
                HushVotingLicenceValidationCodes.ExpectedCurrentInvalid,
                "A baseline payload must not carry an expected-current precondition.");
        }

        return HushVotingLicencePayloadShapeResult.Valid(payload);
    }

    private static HushVotingLicencePayloadShapeResult ValidateUpgrade(HushVotingLicenceAssignmentPayload payload)
    {
        // Upgrade requires BOTH members of the expected-current precondition pair.
        if (payload.ExpectedCurrentLicenceTransactionId is null
            || string.IsNullOrWhiteSpace(payload.ExpectedCurrentPlanId))
        {
            return HushVotingLicencePayloadShapeResult.Invalid(
                HushVotingLicenceValidationCodes.ExpectedCurrentInvalid,
                "An upgrade payload requires the exact expected current licence transaction id and plan id.");
        }

        if (!IsBoundedNonEmpty(payload.ExpectedCurrentPlanId, HushVotingLicencePayloadBounds.MaxPlanIdUtf8Bytes))
        {
            return HushVotingLicencePayloadShapeResult.Invalid(
                HushVotingLicenceValidationCodes.ExpectedCurrentInvalid,
                "Expected current plan id exceeds the bounded plan id length.");
        }

        if (!IsCanonicalUuid(payload.ExpectedCurrentLicenceTransactionId.Value))
        {
            return HushVotingLicencePayloadShapeResult.Invalid(
                HushVotingLicenceValidationCodes.ExpectedCurrentInvalid,
                "Expected current licence transaction id is not a canonical UUID.");
        }

        return HushVotingLicencePayloadShapeResult.Valid(payload);
    }

    private static bool IsBoundedNonEmpty(string? value, int maxUtf8Bytes) =>
        !string.IsNullOrWhiteSpace(value)
        && System.Text.Encoding.UTF8.GetByteCount(value.Trim()) <= maxUtf8Bytes;

    /// <summary>Canonical UUID form: "D" format, invariant lowercase, no surrounding whitespace.</summary>
    private static bool IsCanonicalUuid(Guid value)
    {
        var text = value.ToString("D", CultureInfo.InvariantCulture);
        return string.Equals(text, text.ToLowerInvariant(), StringComparison.Ordinal)
            && Guid.TryParseExact(text, "D", out _);
    }
}
