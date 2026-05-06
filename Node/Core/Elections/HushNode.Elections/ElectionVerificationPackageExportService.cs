using System.Text;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;

namespace HushNode.Elections;

public sealed partial class ElectionVerificationPackageExportService : IElectionVerificationPackageExportService
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
        AddJson(files, VerificationPackageFileNames.Sp04Evidence, BuildSp04Evidence(request), VerificationArtifactVisibility.Public);
        AddJson(files, VerificationPackageFileNames.Sp04ReceiptCommitments, BuildSp04ReceiptCommitments(request), VerificationArtifactVisibility.Public);
        AddJson(files, VerificationPackageFileNames.Sp05EligibilityPolicy, BuildSp05EligibilityPolicy(request), VerificationArtifactVisibility.Public);
        AddJson(files, VerificationPackageFileNames.Sp05EligibilitySummary, BuildSp05EligibilitySummary(request), VerificationArtifactVisibility.Public);
        AddJson(files, VerificationPackageFileNames.Sp05EligibilityVerifierOutput, BuildSp05VerifierOutput(request, exportedAt), VerificationArtifactVisibility.Public);

        if (request.PackageView == VerificationPackageView.RestrictedOwnerAuditor)
        {
            AddJson(
                files,
                VerificationPackageFileNames.RestrictedRosterCheckoff,
                BuildRestrictedRosterCheckoff(request),
                VerificationArtifactVisibility.Restricted);
            AddJson(
                files,
                VerificationPackageFileNames.RestrictedSp04CeremonyRecords,
                BuildRestrictedSp04CeremonyRecords(request),
                VerificationArtifactVisibility.Restricted);
            AddJson(
                files,
                VerificationPackageFileNames.RestrictedSp04PreparedBallotCommitments,
                BuildRestrictedSp04PreparedBallots(request),
                VerificationArtifactVisibility.Restricted);
            AddJson(
                files,
                VerificationPackageFileNames.RestrictedSp04SpoilMarkers,
                BuildRestrictedSp04SpoilMarkers(request),
                VerificationArtifactVisibility.Restricted);
            AddJson(
                files,
                VerificationPackageFileNames.RestrictedRosterImportEvidence,
                BuildRestrictedSp05RosterImportEvidence(request),
                VerificationArtifactVisibility.Restricted);
            AddJson(
                files,
                VerificationPackageFileNames.RestrictedRoster,
                BuildRestrictedSp05Roster(request),
                VerificationArtifactVisibility.Restricted);
            AddJson(
                files,
                VerificationPackageFileNames.RestrictedLinkingEvidence,
                BuildRestrictedSp05LinkingEvidence(request),
                VerificationArtifactVisibility.Restricted);
            AddJson(
                files,
                VerificationPackageFileNames.RestrictedActivationEvents,
                BuildRestrictedSp05ActivationEvents(request),
                VerificationArtifactVisibility.Restricted);
            AddJson(
                files,
                VerificationPackageFileNames.RestrictedCheckoffLedger,
                BuildRestrictedSp05CheckoffLedger(request),
                VerificationArtifactVisibility.Restricted);
            AddJson(
                files,
                VerificationPackageFileNames.RestrictedDisputes,
                new ElectionSp05RestrictedDisputesArtifactRecord(request.Election.ElectionId.ToString(), []),
                VerificationArtifactVisibility.Restricted);
        }

        var manifest = new AuditPackageManifestRecord(
            ManifestVersion: "1.0",
            packageId,
            request.Election.ElectionId.ToString(),
            request.PackageView,
            request.VerifierProfileId,
            exportedAt,
            BuildManifestEntries(files, request.VerifierProfileId));
        var manifestBytes = SerializeToBytes(manifest);
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
        AddJson(files, VerificationPackageFileNames.AuditPackageManifest, manifest, VerificationArtifactVisibility.Public);

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

        if (request.Election.BallotDefinitionVersion is null ||
            request.Election.BallotDefinitionHash is not { Length: > 0 } ||
            request.Election.BallotDefinitionSealedAt is null)
        {
            return Failure(VerificationResultCodes.ChallengeSpoilEvidencePending, "A sealed ballot definition is required for SP-04 verification export.");
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

}
