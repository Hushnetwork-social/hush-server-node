using HushNode.Elections.Storage;
using HushShared.Elections.Model;

namespace HushNode.Elections;

/// <summary>Validates this election's original Open evidence. It never queries a current
/// subscription, display cache or wall clock, and never replaces role/state/proof checks.</summary>
public static class ElectionCapturedEntitlementAuthority
{
    public static async Task<ElectionEntitlementReason> CheckAsync(IElectionsRepository repository, ElectionRecord election)
    {
        var capture = await repository.GetEntitlementCaptureAsync(election.ElectionId);
        if (capture is null) return IndexedOutcome(ElectionEntitlementReason.CaptureUnavailable);
        if (!capture.HasSupportedSemantics()) return IndexedOutcome(ElectionEntitlementReason.SemanticsUnsupported);
        var boundaries = await repository.GetBoundaryArtifactsAsync(election.ElectionId);
        var open = boundaries.SingleOrDefault(b => b.Id == election.OpenArtifactId);
        var basis = await repository.GetEligibilitySnapshotAsync(election.ElectionId, ElectionEligibilitySnapshotType.Open);
        return IndexedOutcome(Check(election, capture, open, basis));
    }

    public static async Task<ElectionCommandResult?> RequireAsync(IElectionsRepository repository, ElectionRecord election)
    {
        var reason = await CheckAsync(repository, election);
        return reason == ElectionEntitlementReason.None ? null : ElectionEntitlementResults.Reject(reason);
    }

    public static bool RequiresCapture(ElectionRecord election) =>
        election.LifecycleState is ElectionLifecycleState.Open or ElectionLifecycleState.Closed or ElectionLifecycleState.Finalized
        || election.OpenedAt.HasValue || election.OpenArtifactId.HasValue;

    private static ElectionEntitlementReason IndexedOutcome(ElectionEntitlementReason reason)
    {
        if (reason != ElectionEntitlementReason.None && HushNode.Indexing.Interfaces.BlockTransactionExecutionScope.Current is not null)
            throw new ElectionIndexAuthorityException("Captured election authorization is missing or inconsistent.");
        return reason;
    }

    public static ElectionEntitlementReason Check(ElectionRecord election, ElectionEntitlementCapture? capture,
        ElectionBoundaryArtifactRecord? open, ElectionEligibilitySnapshotRecord? basis)
    {
        if (capture is null || open is null || basis is null) return ElectionEntitlementReason.CaptureUnavailable;
        if (!capture.HasSupportedSemantics()) return ElectionEntitlementReason.SemanticsUnsupported;
        if (!capture.UnlimitedElectionPolicy || capture.ElectionId != election.ElectionId || capture.SelectedProfileId != election.SelectedProfileId
            || open.Policy.SelectedProfileId != election.SelectedProfileId || open.Policy.GovernanceMode != election.GovernanceMode
            || open.Policy.BindingStatus != election.BindingStatus
            || election.OpenedAt != capture.OpenBlockTimeUtc || election.OpenArtifactId != open.Id
            || open.ElectionId != election.ElectionId || open.ArtifactType != ElectionBoundaryArtifactType.Open
            || open.RecordedAt != capture.OpenBlockTimeUtc || open.SourceTransactionId != capture.OpenTransactionId
            || open.SourceBlockId != capture.OpenBlockId || open.SourceBlockHeight != capture.OpenBlockHeight
            || basis.ElectionId != election.ElectionId || basis.Id != capture.FrozenRosterBasisId
            || basis.SnapshotType != ElectionEligibilitySnapshotType.Open || basis.BoundaryArtifactId != open.Id
            || basis.RosteredCount != capture.FrozenEligibleVoterCount
            || basis.SourceTransactionId != capture.OpenTransactionId || basis.SourceBlockId != capture.OpenBlockId
            || basis.SourceBlockHeight != capture.OpenBlockHeight || basis.RecordedAt != capture.OpenBlockTimeUtc)
            return ElectionEntitlementReason.SemanticsUnsupported;
        return ElectionEntitlementReason.None;
    }
}
