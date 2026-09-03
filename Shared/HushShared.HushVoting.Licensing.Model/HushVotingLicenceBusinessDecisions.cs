namespace HushShared.HushVoting.Licensing.Model;

/// <summary>
/// Pure mapper from a validated internal plan to the client-safe projection used by later
/// authenticated APIs. Excludes internal profile ids, provider keys, database ids, paths, digests,
/// prices, payment fields, internal notes, readiness/legal claims, identities, and history.
/// </summary>
public static class HushVotingLicencePlanProjectionMapper
{
    public static HushVotingLicencePlanProjection ToProjection(HushVotingLicencePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new HushVotingLicencePlanProjection(
            plan.Id,
            plan.Family,
            plan.DisplayName,
            plan.SafeDescription,
            plan.DisplayOrder,
            plan.EligibleVoterCap,
            plan.UnlimitedElections,
            plan.Term,
            plan.Availability,
            plan.UnavailableSafeReason,
            plan.GovernanceOptions
                .Select(HushVotingGovernanceOptionProjection.From)
                .ToArray(),
            plan.CatalogueVersion);
    }

    /// <summary>Project all catalogue plans in deterministic display order.</summary>
    public static IReadOnlyList<HushVotingLicencePlanProjection> ProjectAll(
        HushVotingLicenceCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        return catalogue.Plans.Select(ToProjection).ToArray();
    }
}

/// <summary>
/// Pure resolver from a validated plan/governance option/binding status to the exact runtime
/// ceremony profile id. This is the customer-visible compatibility decision; the host loader in
/// Phase 6 cross-validates it against the approved ceremony-profile registry.
/// </summary>
public static class HushVotingLicenceProfileCompatibilityResolver
{
    /// <summary>Resolve the runtime profile for a plan-authorized option + binding mode.</summary>
    public static HushVotingLicenceProfileResolution Resolve(
        HushVotingLicenceCatalogue catalogue,
        HushVotingLicencePlanId planId,
        HushVotingGovernanceOptionId governanceOptionId,
        HushVotingBindingStatus bindingStatus)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(planId);
        ArgumentNullException.ThrowIfNull(governanceOptionId);

        var plan = catalogue.FindPlan(planId);
        if (plan is null)
        {
            return HushVotingLicenceProfileResolution.Unresolved(
                "LIC_PLAN_UNKNOWN",
                $"Plan '{planId.Value}' is not in the catalogue.");
        }

        if (planId == HushVotingLicencePlanId.Enterprise)
        {
            return HushVotingLicenceProfileResolution.Unresolved(
                "LIC_ENTERPRISE_NOT_EXECUTABLE",
                "Enterprise has no executable governance mapping in v1.");
        }

        if (!plan.HasGovernanceOption(governanceOptionId))
        {
            return HushVotingLicenceProfileResolution.Unresolved(
                "LIC_OPTION_NOT_AUTHORIZED",
                $"Governance option '{governanceOptionId.Value}' is not authorized by plan '{planId.Value}'.");
        }

        var mapping = HushVotingProfileCompatibilityV1.Resolve(governanceOptionId, bindingStatus);
        if (mapping is null)
        {
            return HushVotingLicenceProfileResolution.Unresolved(
                "LIC_BINDING_UNSUPPORTED",
                $"Binding mode '{bindingStatus}' is not supported for option '{governanceOptionId.Value}'.");
        }

        return HushVotingLicenceProfileResolution.Resolved(
            mapping.RuntimeProfileId,
            mapping.DevOnly,
            plan.GetGovernanceOption(governanceOptionId)!.CustomerTrusteeCount,
            plan.GetGovernanceOption(governanceOptionId)!.RequiredApprovalCount);
    }
}

/// <summary>Outcome of a pure profile-compatibility resolution.</summary>
public sealed record HushVotingLicenceProfileResolution(
    bool IsResolved,
    string? RuntimeProfileId,
    bool? DevOnly,
    int? CustomerTrusteeCount,
    int? CustomerRequiredApprovalCount,
    string? StableCode,
    string SafeReason)
{
    public static HushVotingLicenceProfileResolution Resolved(
        string runtimeProfileId,
        bool devOnly,
        int customerTrusteeCount,
        int customerRequiredApprovalCount) =>
        new(
            IsResolved: true,
            runtimeProfileId,
            devOnly,
            customerTrusteeCount,
            customerRequiredApprovalCount,
            StableCode: null,
            SafeReason: "Compatible runtime profile resolved.");

    public static HushVotingLicenceProfileResolution Unresolved(string stableCode, string safeReason) =>
        new(
            IsResolved: false,
            RuntimeProfileId: null,
            DevOnly: null,
            CustomerTrusteeCount: null,
            CustomerRequiredApprovalCount: null,
            stableCode,
            safeReason);
}
