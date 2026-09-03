namespace HushShared.HushVoting.Licensing.Model;

/// <summary>Stable outcome of a governance-choice change request (pure decision, never mutates).</summary>
public sealed record HushVotingGovernanceChangeEvaluation(
    bool Allowed,
    string? StableCode,
    string SafeReason)
{
    public static HushVotingGovernanceChangeEvaluation Allow(string safeReason) =>
        new(true, null, safeReason);

    public static HushVotingGovernanceChangeEvaluation Locked(string stableCode, string safeReason) =>
        new(false, stableCode, safeReason);
}

/// <summary>
/// Pure governance-choice-lock policy consumed by later election-lifecycle features.
/// The owner chooses governance per election. A Draft may switch among options authorized by its
/// current plan only while no ceremony artifact exists (trustee invitation, share, transcript,
/// custody evidence, or equivalent). The first such artifact locks the choice; Open always locks it,
/// even if no artifact was expected. A prohibited change returns a stable rejection and never
/// rewrites or deletes ceremony state.
/// </summary>
public static class HushVotingGovernanceLockEvaluator
{
    public const string GovernanceLockedByArtifact = "GOVERNANCE_LOCKED_BY_CEREMONY_ARTIFACT";
    public const string GovernanceLockedByOpen = "GOVERNANCE_LOCKED_BY_OPEN";
    public const string GovernanceOptionNotAuthorized = "GOVERNANCE_OPTION_NOT_AUTHORIZED";

    /// <summary>
    /// Evaluate whether the governance choice may be changed to <paramref name="requestedOption"/>.
    /// The caller owns Draft/Open state and artifact presence; this evaluator stays pure.
    /// </summary>
    public static HushVotingGovernanceChangeEvaluation Evaluate(
        HushVotingLicencePlan plan,
        HushVotingGovernanceOptionId requestedOption,
        bool isOpen,
        bool hasCeremonyArtifact)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(requestedOption);

        if (!requestedOption.IsKnown)
        {
            return HushVotingGovernanceChangeEvaluation.Locked(
                GovernanceOptionNotAuthorized,
                $"Governance option '{requestedOption.Value}' is unknown.");
        }

        if (!plan.HasGovernanceOption(requestedOption))
        {
            return HushVotingGovernanceChangeEvaluation.Locked(
                GovernanceOptionNotAuthorized,
                $"Governance option '{requestedOption.Value}' is not authorized by plan '{plan.Id.Value}'.");
        }

        if (isOpen)
        {
            return HushVotingGovernanceChangeEvaluation.Locked(
                GovernanceLockedByOpen,
                "An Open election locks its governance choice even when no ceremony artifact was expected.");
        }

        if (hasCeremonyArtifact)
        {
            return HushVotingGovernanceChangeEvaluation.Locked(
                GovernanceLockedByArtifact,
                "The first ceremony artifact locks the governance choice; no ceremony state is rewritten or deleted.");
        }

        return HushVotingGovernanceChangeEvaluation.Allow(
            "The Draft has no ceremony artifact, so an authorized governance change is permitted.");
    }
}
