using System.Text;
using System.Text.Json;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;

namespace HushNode.Elections;

public sealed partial class ElectionVerificationPackageExportService
{
    private static void AddReportArtifacts(
        List<ElectionVerificationPackageFile> files,
        IReadOnlyList<ElectionReportArtifactRecord> artifacts,
        VerificationPackageView packageView)
    {
        foreach (var artifact in artifacts.OrderBy(x => x.SortOrder).ThenBy(x => x.FileName, StringComparer.Ordinal))
        {
            var visibility = artifact.AccessScope == ElectionReportArtifactAccessScope.OwnerAuditorOnly
                ? VerificationArtifactVisibility.Restricted
                : VerificationArtifactVisibility.Public;
            if (visibility == VerificationArtifactVisibility.Restricted &&
                packageView != VerificationPackageView.RestrictedOwnerAuditor)
            {
                continue;
            }

            files.Add(new ElectionVerificationPackageFile(
                $"{VerificationPackageFileNames.ReportPackageDirectory}/{artifact.FileName}",
                artifact.MediaType,
                visibility,
                Encoding.UTF8.GetBytes(artifact.Content)));
        }
    }

    private static void AddAbnormalFinalizationEvidence(
        List<ElectionVerificationPackageFile> files,
        ElectionVerificationPackageExportRequest request)
    {
        var evidence = ResolveAbnormalFinalizationEvidence(request);
        if (evidence is null)
        {
            return;
        }

        files.RemoveAll(x => string.Equals(
            x.RelativePath,
            VerificationPackageFileNames.ReportPackageAbnormalFinalizationEvidence,
            StringComparison.Ordinal));
        AddJson(
            files,
            VerificationPackageFileNames.ReportPackageAbnormalFinalizationEvidence,
            evidence,
            VerificationArtifactVisibility.Public);
    }

    private static AbnormalFinalizationEvidenceArtifactRecord? ResolveAbnormalFinalizationEvidence(
        ElectionVerificationPackageExportRequest request)
    {
        if (request.AbnormalFinalizationEvidence is not null)
        {
            return request.AbnormalFinalizationEvidence;
        }

        var artifact = request.ReportArtifacts
            .Where(x =>
                x.ArtifactKind == ElectionReportArtifactKind.MachineAbnormalFinalizationEvidence ||
                string.Equals(x.FileName, "abnormal-finalization-evidence.json", StringComparison.Ordinal))
            .OrderByDescending(x => x.RecordedAt)
            .FirstOrDefault();

        return artifact is null
            ? null
            : JsonSerializer.Deserialize<AbnormalFinalizationEvidenceArtifactRecord>(
                artifact.Content,
                VerificationJson.Options);
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

