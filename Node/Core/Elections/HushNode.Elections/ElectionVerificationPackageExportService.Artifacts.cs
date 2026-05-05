using HushShared.Elections.Verification.Model;

namespace HushNode.Elections;

public sealed partial class ElectionVerificationPackageExportService
{
    private static ElectionRecordReferenceRecord BuildElectionRecord(
        ElectionVerificationPackageExportRequest request)
    {
        var binding = request.ProtocolPackageBinding!;
        return new ElectionRecordReferenceRecord(
            request.Election.ElectionId.ToString(),
            request.Election.LifecycleState.ToString(),
            binding.PackageId,
            binding.PackageVersion,
            binding.PackageApprovalStatus.ToString(),
            binding.SpecPackageHash,
            binding.ProofPackageHash,
            binding.ReleaseManifestHash,
            binding.SpecAccessLocations
                .Concat(binding.ProofAccessLocations)
                .Select(x => new VerificationAccessLocationRecord(x.LocationKind.ToString(), x.Location, x.ContentHash))
                .ToArray());
    }

    private static VerifierProfileRecord BuildProfile(string profileId)
    {
        var highAssurance = string.Equals(profileId, VerificationProfileIds.HighAssuranceV1, StringComparison.Ordinal);
        var restricted = string.Equals(profileId, VerificationProfileIds.RestrictedOwnerAuditorV1, StringComparison.Ordinal);
        return new VerifierProfileRecord(
            profileId,
            profileId,
            AllowsDraftProtocolPackage: !highAssurance,
            AllowsPendingLaterFeatureEvidence: !highAssurance,
            RequiresRestrictedEvidence: restricted,
            RequiresHighAssuranceEvidence: highAssurance,
            RequiredCheckCodes:
            [
                "VFY-MAN-000",
                "VFY-ELECTION-000",
                "VFY-ACCEPTED-000",
                "VFY-PUBLISHED-000",
                "VFY-PRIVACY-000",
            ]);
    }

    private static AcceptedBallotSetArtifactRecord BuildAcceptedBallotSet(
        ElectionVerificationPackageExportRequest request) =>
        new(
            request.Election.ElectionId.ToString(),
            request.AcceptedBallots.Count,
            VerificationCanonicalHash.ToLowerHex(
                VerificationCanonicalHash.ComputeAcceptedBallotInventoryHash(request.AcceptedBallots)),
            request.AcceptedBallots
                .OrderBy(x => x.BallotNullifier, StringComparer.Ordinal)
                .Select(x => new AcceptedBallotArtifactRecord(
                    x.BallotNullifier,
                    x.EncryptedBallotPackage,
                    x.ProofBundle,
                    VerificationCanonicalHash.ComputeSha256UpperHex(x.EncryptedBallotPackage),
                    VerificationCanonicalHash.ComputeSha256UpperHex(x.ProofBundle)))
                .ToArray());

    private static PublishedBallotStreamArtifactRecord BuildPublishedBallotStream(
        ElectionVerificationPackageExportRequest request) =>
        new(
            request.Election.ElectionId.ToString(),
            request.PublishedBallots.Count,
            VerificationCanonicalHash.ToLowerHex(
                VerificationCanonicalHash.ComputePublishedBallotStreamHash(request.PublishedBallots)),
            request.PublishedBallots
                .OrderBy(x => x.PublicationSequence)
                .Select(x => new PublishedBallotArtifactRecord(
                    x.PublicationSequence,
                    x.EncryptedBallotPackage,
                    x.ProofBundle,
                    VerificationCanonicalHash.ComputeSha256UpperHex(x.EncryptedBallotPackage),
                    VerificationCanonicalHash.ComputeSha256UpperHex(x.ProofBundle)))
                .ToArray());

    private static TallyReplayArtifactRecord BuildTallyReplay(ElectionVerificationPackageExportRequest request)
    {
        var highAssurance = string.Equals(request.VerifierProfileId, VerificationProfileIds.HighAssuranceV1, StringComparison.Ordinal);
        return new TallyReplayArtifactRecord(
            request.Election.ElectionId.ToString(),
            PublicationProofMode: "zk_rerandomization_shuffle_v1",
            highAssurance ? VerificationCheckStatus.Fail : VerificationCheckStatus.Warn,
            VerificationResultCodes.PublicationProofEvidencePending,
            highAssurance
                ? "High-assurance profile requires SP-07 publication proof evidence."
                : "SP-07 publication proof evidence is pending a later protocol package revision.");
    }

    private static TrusteeReleaseEvidenceArtifactRecord BuildTrusteeReleaseEvidence(
        ElectionVerificationPackageExportRequest request) =>
        new(
            request.Election.ElectionId.ToString(),
            request.FinalizationSessions.Count,
            request.FinalizationShares.Count(x => x.IsAccepted),
            request.FinalizationShares
                .Where(x => x.IsAccepted)
                .OrderBy(x => x.ShareIndex)
                .Select(x => new TrusteeReleaseShareEvidenceRecord(
                    x.TrusteeUserAddress,
                    x.ShareIndex,
                    x.ShareMaterialHash,
                    x.Status.ToString()))
                .ToArray());

    private static ResultBindingArtifactRecord BuildResultBinding(ElectionVerificationPackageExportRequest request) =>
        new(
            request.Election.ElectionId.ToString(),
            request.ReportPackage!.Id.ToString(),
            VerificationCanonicalHash.ToLowerHex(request.ReportPackage.PackageHash),
            request.Election.FinalizeArtifactId?.ToString(),
            request.Election.OfficialResultArtifactId?.ToString(),
            request.Election.UnofficialResultArtifactId?.ToString());

    private static RestrictedRosterCheckoffArtifactRecord BuildRestrictedRosterCheckoff(
        ElectionVerificationPackageExportRequest request)
    {
        var participation = request.ParticipationRecords.ToDictionary(
            x => x.OrganizationVoterId,
            StringComparer.OrdinalIgnoreCase);
        return new RestrictedRosterCheckoffArtifactRecord(
            request.Election.ElectionId.ToString(),
            request.RosterEntries
                .OrderBy(x => x.OrganizationVoterId, StringComparer.OrdinalIgnoreCase)
                .Select(x => new RestrictedRosterCheckoffEntryRecord(
                    x.OrganizationVoterId,
                    x.LinkedActorPublicAddress,
                    x.VotingRightStatus.ToString(),
                    participation.GetValueOrDefault(x.OrganizationVoterId)?.ParticipationStatus.ToString()))
                .ToArray());
    }
}
