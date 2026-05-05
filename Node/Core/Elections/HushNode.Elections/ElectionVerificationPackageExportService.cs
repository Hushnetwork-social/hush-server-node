using System.Text;
using System.Text.Json;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;

namespace HushNode.Elections;

public sealed class ElectionVerificationPackageExportService : IElectionVerificationPackageExportService
{
    public ElectionVerificationPackageExportResult Export(ElectionVerificationPackageExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationFailure = ValidateRequest(request);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        var exportedAt = request.ExportedAt ?? DateTime.UtcNow;
        var packageId = $"HushElectionPackage-{request.Election.ElectionId}";
        var files = new List<ElectionVerificationPackageFile>();

        AddJson(files, VerificationPackageFileNames.ElectionRecord, BuildElectionRecord(request), VerificationArtifactVisibility.Public);
        AddJson(files, VerificationPackageFileNames.VerifierProfile, BuildProfile(request.VerifierProfileId), VerificationArtifactVisibility.Public);
        AddReportArtifacts(files, request.ReportArtifacts);
        AddJson(files, VerificationPackageFileNames.AcceptedBallotSet, BuildAcceptedBallotSet(request), VerificationArtifactVisibility.Public);
        AddJson(files, VerificationPackageFileNames.PublishedBallotStream, BuildPublishedBallotStream(request), VerificationArtifactVisibility.Public);
        AddJson(files, VerificationPackageFileNames.TallyReplay, BuildTallyReplay(request), VerificationArtifactVisibility.Public);
        AddJson(files, VerificationPackageFileNames.TrusteeReleaseEvidence, BuildTrusteeReleaseEvidence(request), VerificationArtifactVisibility.Public);
        AddJson(files, VerificationPackageFileNames.ResultBinding, BuildResultBinding(request), VerificationArtifactVisibility.Public);

        if (request.PackageView == VerificationPackageView.RestrictedOwnerAuditor)
        {
            AddJson(
                files,
                VerificationPackageFileNames.RestrictedRosterCheckoff,
                BuildRestrictedRosterCheckoff(request),
                VerificationArtifactVisibility.Restricted);
        }

        var provisionalManifestEntries = BuildManifestEntries(files, request.VerifierProfileId);
        var provisionalManifest = new AuditPackageManifestRecord(
            ManifestVersion: "1.0",
            packageId,
            request.Election.ElectionId.ToString(),
            request.PackageView,
            request.VerifierProfileId,
            exportedAt,
            provisionalManifestEntries);
        var manifestBytes = SerializeToBytes(provisionalManifest);
        var manifestHash = VerificationCanonicalHash.ComputeManifestFileSha256(manifestBytes);
        var inputManifest = new VerifierInputManifestRecord(
            ManifestVersion: "1.0",
            packageId,
            request.Election.ElectionId.ToString(),
            request.PackageView,
            request.VerifierProfileId,
            manifestHash,
            VerificationPackageFileNames.RootFiles,
            request.PackageView == VerificationPackageView.RestrictedOwnerAuditor
                ? VerificationPackageFileNames.RestrictedArtifactDirectories
                : VerificationPackageFileNames.ArtifactDirectories);

        AddJson(files, VerificationPackageFileNames.VerifierInputManifest, inputManifest, VerificationArtifactVisibility.Public);

        var finalManifest = provisionalManifest with
        {
            Entries = BuildManifestEntries(files, request.VerifierProfileId),
        };
        AddJson(files, VerificationPackageFileNames.AuditPackageManifest, finalManifest, VerificationArtifactVisibility.Public);

        var packageHash = VerificationCanonicalHash.ComputeManifestFileSha256(
            Encoding.UTF8.GetBytes(string.Join(
                '\n',
                files
                    .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
                    .Select(x => $"{x.RelativePath}|{VerificationCanonicalHash.ComputeManifestFileSha256(x.Content)}"))));

        return new ElectionVerificationPackageExportResult(
            Success: true,
            Code: VerificationResultCodes.PackageStructureValid,
            Message: "Verification package exported.",
            packageId,
            packageHash,
            files.OrderBy(x => x.RelativePath, StringComparer.Ordinal).ToArray());
    }

    public static void WritePackageToDirectory(
        ElectionVerificationPackageExportResult result,
        string packagePath)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!result.Success)
        {
            throw new InvalidOperationException("Cannot write a failed verification package export.");
        }

        Directory.CreateDirectory(packagePath);
        foreach (var file in result.Files)
        {
            var fullPath = Path.Combine(packagePath, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, file.Content);
        }
    }

    private static ElectionVerificationPackageExportResult? ValidateRequest(
        ElectionVerificationPackageExportRequest request)
    {
        if (request.Election.LifecycleState != ElectionLifecycleState.Finalized)
        {
            return Failure(VerificationResultCodes.ElectionNotFinalized, "Election must be finalized before verification package export.");
        }

        if (request.ReportPackage is null || request.ReportPackage.Status != ElectionReportPackageStatus.Sealed)
        {
            return Failure(VerificationResultCodes.PackageManifestMissingArtifact, "A sealed report package is required.");
        }

        if (request.ProtocolPackageBinding is null ||
            request.ProtocolPackageBinding.Status != ProtocolPackageBindingStatus.Sealed)
        {
            return Failure(VerificationResultCodes.VerifierProfilePackageMismatch, "Sealed protocol package refs are required.");
        }

        if (request.PackageView == VerificationPackageView.RestrictedOwnerAuditor &&
            !request.RestrictedAccessAuthorized)
        {
            return Failure(VerificationResultCodes.RestrictedExportUnauthorized, "Restricted package export requires an authorized actor.");
        }

        if (!VerificationProfileIds.All.Contains(request.VerifierProfileId))
        {
            return Failure(VerificationResultCodes.VerifierProfilePackageMismatch, "Unsupported verifier profile.");
        }

        return null;
    }

    private static ElectionVerificationPackageExportResult Failure(string code, string message) =>
        new(
            Success: false,
            code,
            message,
            PackageId: null,
            PackageHash: null,
            Files: []);

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
            VerificationResultCodes.PublicationProofPendingFeat117,
            highAssurance
                ? "High-assurance profile requires SP-07 publication proof evidence."
                : "SP-07 publication proof evidence is pending FEAT-117.");
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

    private static void AddReportArtifacts(
        List<ElectionVerificationPackageFile> files,
        IReadOnlyList<ElectionReportArtifactRecord> artifacts)
    {
        foreach (var artifact in artifacts.OrderBy(x => x.SortOrder).ThenBy(x => x.FileName, StringComparer.Ordinal))
        {
            var visibility = artifact.AccessScope == ElectionReportArtifactAccessScope.OwnerAuditorOnly
                ? VerificationArtifactVisibility.Restricted
                : VerificationArtifactVisibility.Public;
            if (visibility == VerificationArtifactVisibility.Restricted)
            {
                continue;
            }

            files.Add(new ElectionVerificationPackageFile(
                $"{VerificationPackageFileNames.ReportPackageDirectory}/{artifact.FileName}",
                artifact.MediaType,
                VerificationArtifactVisibility.Public,
                Encoding.UTF8.GetBytes(artifact.Content)));
        }
    }

    private static IReadOnlyList<AuditPackageManifestEntryRecord> BuildManifestEntries(
        IReadOnlyList<ElectionVerificationPackageFile> files,
        string verifierProfileId) =>
        files
            .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
            .Select(x => new AuditPackageManifestEntryRecord(
                x.RelativePath,
                VerificationCanonicalHash.ComputeManifestFileSha256(x.Content),
                x.Content.Length,
                x.MediaType,
                x.Visibility,
                VerificationEvidenceRequirement.Required,
                RequiredProfileIds:
                [
                    verifierProfileId,
                ]))
            .ToArray();

    private static void AddJson<T>(
        List<ElectionVerificationPackageFile> files,
        string relativePath,
        T value,
        VerificationArtifactVisibility visibility) =>
        files.Add(new ElectionVerificationPackageFile(
            relativePath,
            "application/json",
            visibility,
            SerializeToBytes(value)));

    private static byte[] SerializeToBytes<T>(T value) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, VerificationJson.Options));
}
