using System.Security.Cryptography;
using System.Text;
using HushShared.HushVoting.Licensing.Model;

namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// Pure, side-effect-free entitlement decisions. Everything here is unit-testable without a
/// database and locks the FEAT-012 term/plan semantics used by the transaction coordinator.
/// </summary>
public static class LicenceEntitlementDecisions
{
    /// <summary>Stable assignment lifecycle reason when an annual assignment expires.</summary>
    public const string ReasonAnnualExpiry = "annual_expiry";

    /// <summary>Stable assignment lifecycle reason when a higher plan supersedes the current assignment.</summary>
    public const string ReasonSupersededByAutomaticUpgrade = "superseded_by_automatic_upgrade";

    /// <summary>
    /// Lazy-migration provenance decision: an identity created at or before the rollout watermark is
    /// provisioned with migration provenance; anything after the watermark is a plain default.
    /// </summary>
    public static string DecideProvisionSource(long identityCreationBlockIndex, long rolloutWatermarkBlockHeight)
    {
        if (identityCreationBlockIndex <= rolloutWatermarkBlockHeight)
        {
            return LicencePersistenceVocabulary.SourceMigrationLazyDefault;
        }

        return LicencePersistenceVocabulary.SourceDefaultFree;
    }

    /// <summary>
    /// Upper-exclusive expiry instant for an assignment beginning at <paramref name="effectiveFromUtc"/>
    /// under a whole-calendar-years term. Uses DateTime.AddYears (calendar semantics, never 365 fixed
    /// days, leap-day safe). Returns null for perpetual terms.
    /// </summary>
    public static DateTime? ComputeExpiryInstant(DateTime effectiveFromUtc, HushVotingLicenceTerm term)
    {
        if (term.IsPerpetual)
        {
            return null;
        }

        if (term.Kind != HushVotingLicenceTermKind.CalendarYears || term.Years < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(term), "Only perpetual or whole-calendar-year terms are assignable.");
        }

        // DateTime.AddYears handles leap days: 2024-02-29 + 1 year = 2025-02-28 (upper-exclusive).
        return effectiveFromUtc.AddYears(term.Years);
    }

    /// <summary>
    /// An annual assignment is expired at <paramref name="nowUtc"/> when now is at or after its
    /// upper-exclusive expiry instant. Perpetual assignments never expire.
    /// </summary>
    public static bool IsExpired(LicenceAssignmentEntity assignment, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        if (assignment.TermKind != LicencePersistenceVocabulary.TermAnnual)
        {
            return false;
        }

        return assignment.ExpiresAtUtc is DateTime expiresAt && nowUtc >= expiresAt;
    }

    /// <summary>Maps a FEAT-012 plan to the immutable operative snapshot persisted at assignment time.</summary>
    public static LicenceOperativeSnapshot ToOperativeSnapshot(HushVotingLicencePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var family = plan.Family switch
        {
            HushVotingLicenceFamily.Direct => LicencePersistenceVocabulary.PlanFamilyDirect,
            HushVotingLicenceFamily.Veritas => LicencePersistenceVocabulary.PlanFamilyVeritas,
            HushVotingLicenceFamily.Enterprise => LicencePersistenceVocabulary.PlanFamilyEnterprise,
            _ => throw new ArgumentOutOfRangeException(nameof(plan), "Unknown plan family."),
        };

        var termKind = plan.Term.IsPerpetual
            ? LicencePersistenceVocabulary.TermPerpetual
            : LicencePersistenceVocabulary.TermAnnual;

        return new LicenceOperativeSnapshot(
            family,
            plan.UpgradeRank,
            plan.EligibleVoterCap,
            plan.UnlimitedElections,
            termKind,
            plan.Term.Years,
            plan.GovernanceOptions.Select(static o => o.Id.Value).ToArray());
    }

    /// <summary>
    /// Maps a FEAT-012 upgrade evaluation rejection to the closed durable activation result.
    /// Same-plan requests stay unchanged; lower/not-Veritas transitions are not-higher; unknown plans
    /// are plan_unknown; Enterprise/non-automatic/unavailable targets are plan_unavailable.
    /// </summary>
    public static LicenceActivationOutcome MapUpgradeEvaluationToDurableResult(HushVotingLicenceUpgradeEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);

        if (evaluation.Allowed)
        {
            return LicenceActivationOutcome.Activated;
        }

        return evaluation.StableCode switch
        {
            HushVotingLicenceUpgradeEvaluator.UpgradeUnknownPlan => LicenceActivationOutcome.PlanUnknown,
            HushVotingLicenceUpgradeEvaluator.UpgradeTargetInvalid => LicenceActivationOutcome.TransitionUnchanged,
            HushVotingLicenceUpgradeEvaluator.UpgradeNotVeritas => LicenceActivationOutcome.TransitionNotHigher,
            HushVotingLicenceUpgradeEvaluator.UpgradeNotHigherRank => LicenceActivationOutcome.TransitionNotHigher,
            HushVotingLicenceUpgradeEvaluator.UpgradeEnterpriseNotActionable => LicenceActivationOutcome.PlanUnavailable,
            HushVotingLicenceUpgradeEvaluator.UpgradeNotAutomatic => LicenceActivationOutcome.PlanUnavailable,
            HushVotingLicenceUpgradeEvaluator.UpgradeTargetNotAvailable => LicenceActivationOutcome.PlanUnavailable,
            _ => LicenceActivationOutcome.PlanUnavailable,
        };
    }

    /// <summary>
    /// Canonical SHA-256 payload fingerprint (uppercase hex) for an activation command. Length-prefixed
    /// fields make the canonical form unambiguous; byte-equivalent payloads always fingerprint equal.
    /// </summary>
    public static string CanonicalActivationFingerprint(
        string expectedCurrentPlanId,
        long expectedEntitlementRevision,
        string requestedTargetPlanId)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrentPlanId);
        ArgumentNullException.ThrowIfNull(requestedTargetPlanId);

        var canonical = CultureInvariantBuffer(expectedCurrentPlanId, expectedEntitlementRevision, requestedTargetPlanId);

        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static string CultureInvariantBuffer(
        string expectedCurrentPlanId,
        long expectedEntitlementRevision,
        string requestedTargetPlanId)
    {
        var builder = new StringBuilder(160);
        builder.Append(expectedCurrentPlanId.Length).Append(':').Append(expectedCurrentPlanId);
        builder.Append('|');
        builder.Append(expectedEntitlementRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.Append('|');
        builder.Append(requestedTargetPlanId.Length).Append(':').Append(requestedTargetPlanId);
        return builder.ToString();
    }

    /// <summary>Maps a durable operation-result string to its activation outcome for replay.</summary>
    public static LicenceActivationOutcome FromDurableResult(string durableResult) =>
        LicenceEntitlementOutcomeNames.FromDurableResultString(durableResult);
}

/// <summary>Immutable assignment-time operative snapshot (pinned; never silently changed).</summary>
public sealed record LicenceOperativeSnapshot(
    string PlanFamily,
    int UpgradeRank,
    int? EligibleVoterCap,
    bool UnlimitedElectionPolicy,
    string TermKind,
    int TermYears,
    IReadOnlyList<string> AllowedGovernanceOptionIds);
