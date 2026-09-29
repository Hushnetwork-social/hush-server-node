using HushNode.Elections.Storage;
using HushShared.Elections.Model;

namespace HushNode.Elections;

/// <summary>Fresh advisory intersection of captured authority, authenticated actor role and
/// election state. Never a transferable token: each mutation still authorizes independently.</summary>
public static class ElectionScopedAccessProjector
{
    public static async Task<ElectionScopedAccessView?> ReadAsync(IElectionsRepository repository,
        ElectionRecord election, string? authenticatedActor)
    {
        if (string.IsNullOrWhiteSpace(authenticatedActor)) return null;
        if (!ElectionCapturedEntitlementAuthority.RequiresCapture(election))
            return new(election.ElectionId, 1, [], "");
        var reason = await ElectionCapturedEntitlementAuthority.CheckAsync(repository, election);
        if (reason != ElectionEntitlementReason.None)
            return new(election.ElectionId, 1, [], ElectionEntitlementReasonNames.ToWire(reason));

        var owner = string.Equals(authenticatedActor, election.OwnerPublicAddress, StringComparison.Ordinal);
        var invitations = await repository.GetTrusteeInvitationsAsync(election.ElectionId);
        var trustee = invitations.Any(t => t.Status == ElectionTrusteeInvitationStatus.Accepted
            && t.TrusteeUserAddress == authenticatedActor);
        var continuity = trustee ? await repository.GetCurrentTrusteeContinuityDecisionAsync(election.ElectionId, authenticatedActor) : null;
        var grant = await repository.GetReportAccessGrantAsync(election.ElectionId, authenticatedActor);
        var auditor = grant?.GrantRole == ElectionReportAccessGrantRole.DesignatedAuditor;
        var roster = await repository.GetRosterEntryByLinkedActorAsync(election.ElectionId, authenticatedActor);
        var voter = roster is not null;
        var canVote = roster is { WasPresentAtOpen: true } && (election.EligibilityMutationPolicy switch
        {
            EligibilityMutationPolicy.FrozenAtOpen => roster.WasActiveAtOpen,
            EligibilityMutationPolicy.LateActivationForRosteredVotersOnly => roster.IsActive,
            _ => false,
        });
        return Project(election, owner, trustee, auditor, voter, canVote, continuity?.BlocksThresholdActions == true);
    }

    public static ElectionScopedAccessView Project(ElectionRecord election, bool owner, bool trustee,
        bool auditor, bool voter, bool eligibleVoter, bool trusteeBlocked)
    {
        var operations = new List<string>();
        if (owner || trustee || auditor) operations.AddRange(["audit", "report"]);
        if (owner || trustee || auditor || voter) operations.Add("results");
        if (election.LifecycleState == ElectionLifecycleState.Open)
        {
            if (eligibleVoter && !election.VoteAcceptanceLockedAt.HasValue) operations.Add("vote");
            if (owner) operations.Add("close");
        }
        if (election.LifecycleState is ElectionLifecycleState.Open or ElectionLifecycleState.Closed)
        {
            if (owner) operations.AddRange(["continuity", "void"]);
            if (trustee) operations.Add("continuity");
            if (trustee && !trusteeBlocked && election.GovernanceMode == ElectionGovernanceMode.TrusteeThreshold) operations.Add("approve");
        }
        if (election.LifecycleState == ElectionLifecycleState.Closed)
        {
            if (owner && election.TallyReadyAt.HasValue) operations.Add("finalize");
            if (trustee && !trusteeBlocked && election.GovernanceMode == ElectionGovernanceMode.TrusteeThreshold) operations.Add("submitFinalizationShare");
        }
        return new(election.ElectionId, 1, operations.Distinct(StringComparer.Ordinal).ToArray(), "");
    }
}
