using System.Security.Cryptography;
using System.Text.Json;
using HushNode.Elections;
using HushNode.HushVoting.Licensing.Storage;
using HushShared.Elections.Model;
using HushShared.HushVoting.Licensing.Model;

namespace HushServerNode.Tests.Elections;

// Explicit Given data for existing unit cases that start after Open. No production or
// repository fallback synthesizes authority. Missing-capture tests omit/remove this data.
internal static class HistoricalOpenAuthorizationFixture
{
    public static void Seed(
        Dictionary<ElectionId, ElectionRecord> elections, List<ElectionEntitlementCapture> captures,
        List<ElectionBoundaryArtifactRecord> boundaries, List<ElectionEligibilitySnapshotRecord> snapshots,
        IReadOnlyList<ElectionRosterEntryRecord>? roster = null)
    {
        foreach (var original in elections.Values.ToArray())
        {
            if (!ElectionCapturedEntitlementAuthority.RequiresCapture(original)
                || captures.Any(c => c.ElectionId == original.ElectionId)) continue;
            var open = boundaries.SingleOrDefault(b => b.Id == original.OpenArtifactId);
            var at = original.OpenedAt ?? open?.RecordedAt ?? original.CreatedAt;
            var tx = open?.SourceTransactionId ?? Guid.NewGuid();
            var block = open?.SourceBlockId ?? Guid.NewGuid();
            var height = open?.SourceBlockHeight ?? 1;
            open ??= ElectionModelFactory.CreateBoundaryArtifact(ElectionBoundaryArtifactType.Open,
                original, original.OwnerPublicAddress, recordedAt: at);
            // Existing Given builders can select a profile after constructing their
            // Open snapshot. Seal those explicit fixture choices together before Act.
            open = open with { RecordedAt = at, SourceTransactionId = tx, SourceBlockId = block, SourceBlockHeight = height,
                Policy = open.Policy with { SelectedProfileId = original.SelectedProfileId,
                    GovernanceMode = original.GovernanceMode, BindingStatus = original.BindingStatus } };
            boundaries.RemoveAll(b => b.Id == open.Id);
            boundaries.Add(open);
            var election = original with { OpenedAt = at, OpenArtifactId = open.Id };
            elections[election.ElectionId] = election;
            var count = roster?.Count(r => r.ElectionId == election.ElectionId) ?? 0;
            var basis = snapshots.SingleOrDefault(b => b.ElectionId == election.ElectionId
                && b.SnapshotType == ElectionEligibilitySnapshotType.Open)
                ?? ElectionModelFactory.CreateEligibilitySnapshot(election.ElectionId, ElectionEligibilitySnapshotType.Open,
                    election.EligibilityMutationPolicy, count, 0, 0, 0, 0, 0,
                    SHA256.HashData([]), SHA256.HashData([]), SHA256.HashData([]), election.OwnerPublicAddress);
            basis = basis with { BoundaryArtifactId = open.Id, RecordedAt = at, SourceTransactionId = tx,
                SourceBlockId = block, SourceBlockHeight = height };
            snapshots.RemoveAll(b => b.Id == basis.Id);
            snapshots.Add(basis);
            var option = election.GovernanceMode == ElectionGovernanceMode.AdminOnly
                ? HushVotingGovernanceOptionId.NoCustomerTrustees.Value : HushVotingGovernanceOptionId.Trustees3Of5.Value;
            var release = LicenceServiceConfiguration.CreateDefault();
            captures.Add(new(election.ElectionId, Guid.NewGuid(), Guid.NewGuid(),
                HushVotingLicencePlanId.Veritas10000.Value, "veritas", 3, 10000, true, "annual", 1,
                at.AddMonths(-1), at.AddMonths(11), JsonSerializer.Serialize(new[] { option }),
                release.CatalogueVersion, release.ReleaseDigestSha256, 1, election.SelectedProfileId, option,
                basis.RosteredCount, basis.Id, tx, block, height, 0, at));
        }
    }

}
