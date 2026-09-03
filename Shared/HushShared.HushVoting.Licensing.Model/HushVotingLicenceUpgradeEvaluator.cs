namespace HushShared.HushVoting.Licensing.Model;

/// <summary>Stable outcome of a pure licence upgrade evaluation (never mutates or persists).</summary>
public sealed record HushVotingLicenceUpgradeEvaluation(
    bool Allowed,
    string? StableCode,
    string SafeReason,
    HushVotingLicencePlanId? CurrentPlanId,
    HushVotingLicencePlanId? TargetPlanId)
{
    public static HushVotingLicenceUpgradeEvaluation Allow(
        HushVotingLicencePlanId current,
        HushVotingLicencePlanId target) =>
        new(true, null, "Automatic higher-plan activation is allowed.", current, target);

    public static HushVotingLicenceUpgradeEvaluation Reject(
        HushVotingLicencePlanId current,
        HushVotingLicencePlanId? target,
        string stableCode,
        string safeReason) =>
        new(false, stableCode, safeReason, current, target);
}

/// <summary>
/// Pure automatic-upgrade evaluator. It only decides; it never assigns, persists, calculates
/// effective dates, or performs the activation. Rules (frozen from EPIC-002 deep-dive):
///   - the target must exist in the catalogue, be AutomaticUpgrade, in the Veritas family, and have
///     a strictly greater rank than the current plan;
///   - Direct Free may target any Veritas plan; Veritas 500 may target 2k or 10k; Veritas 2k may
///     target 10k (sequential upgrades are not required);
///   - current-plan, lower-plan, Direct Free downgrade, same-plan renewal, Enterprise activation,
///     disabled, unknown, and not-Veritas targets are rejected with stable non-mutating results.
/// </summary>
public static class HushVotingLicenceUpgradeEvaluator
{
    public const string UpgradeTargetInvalid = "UPGRADE_TARGET_INVALID";
    public const string UpgradeTargetNotAvailable = "UPGRADE_TARGET_NOT_AVAILABLE";
    public const string UpgradeNotHigherRank = "UPGRADE_NOT_HIGHER_RANK";
    public const string UpgradeNotAutomatic = "UPGRADE_NOT_AUTOMATIC";
    public const string UpgradeNotVeritas = "UPGRADE_NOT_VERITAS";
    public const string UpgradeEnterpriseNotActionable = "UPGRADE_ENTERPRISE_NOT_ACTIONABLE";
    public const string UpgradeUnknownPlan = "UPGRADE_UNKNOWN_PLAN";

    /// <summary>Evaluate a transition within a catalogue snapshot. Pure and side-effect free.</summary>
    public static HushVotingLicenceUpgradeEvaluation Evaluate(
        HushVotingLicenceCatalogue catalogue,
        HushVotingLicencePlanId currentPlanId,
        HushVotingLicencePlanId targetPlanId)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(currentPlanId);
        ArgumentNullException.ThrowIfNull(targetPlanId);

        var current = catalogue.FindPlan(currentPlanId);
        var target = catalogue.FindPlan(targetPlanId);

        if (current is null)
        {
            return HushVotingLicenceUpgradeEvaluation.Reject(
                currentPlanId,
                null,
                UpgradeUnknownPlan,
                $"Current plan '{currentPlanId.Value}' is not in the catalogue.");
        }

        if (target is null || !targetPlanId.IsKnown)
        {
            return HushVotingLicenceUpgradeEvaluation.Reject(
                currentPlanId,
                targetPlanId,
                UpgradeUnknownPlan,
                $"Target plan '{targetPlanId.Value}' is unknown or not in the catalogue.");
        }

        if (targetPlanId == currentPlanId)
        {
            return HushVotingLicenceUpgradeEvaluation.Reject(
                currentPlanId,
                targetPlanId,
                UpgradeTargetInvalid,
                "Same-plan renewal is not an actionable transition in v1.");
        }

        if (target.Family == HushVotingLicenceFamily.Enterprise)
        {
            return HushVotingLicenceUpgradeEvaluation.Reject(
                currentPlanId,
                targetPlanId,
                UpgradeEnterpriseNotActionable,
                "Enterprise activation is not actionable in v1.");
        }

        if (target.Family != HushVotingLicenceFamily.Veritas)
        {
            return HushVotingLicenceUpgradeEvaluation.Reject(
                currentPlanId,
                targetPlanId,
                UpgradeNotVeritas,
                "Only Veritas plans are valid automatic higher-plan targets.");
        }

        if (target.Availability != HushVotingLicenceAvailability.AutomaticUpgrade)
        {
            return HushVotingLicenceUpgradeEvaluation.Reject(
                currentPlanId,
                targetPlanId,
                UpgradeNotAutomatic,
                $"Target plan '{targetPlanId.Value}' is not an AutomaticUpgrade plan.");
        }

        if (target.UpgradeRank <= current.UpgradeRank)
        {
            return HushVotingLicenceUpgradeEvaluation.Reject(
                currentPlanId,
                targetPlanId,
                UpgradeNotHigherRank,
                "Automatic activation requires a strictly greater plan rank.");
        }

        // Allowed pair table (six transitions). Direct Free may target any Veritas; 500 -> 2k/10k;
        // 2k -> 10k. Because ranks are unique and increasing in this family and we require a greater
        // rank with AutomaticUpgrade, the rank check above already implements the pair table.
        return HushVotingLicenceUpgradeEvaluation.Allow(currentPlanId, targetPlanId);
    }
}
