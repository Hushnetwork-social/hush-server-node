using System.Text.Json;

namespace HushShared.Elections.Verification.Model;

public sealed partial class HushVotingPackageVerifier
{
    private static async Task<HushVotingPackageVerificationResult> VerifyVoidPackageAsync(
        HushVotingPackageVerificationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var manifest = await ReadJsonAsync<VoidPackageManifestRecord>(
                request.PackagePath,
                VerificationPackageFileNames.VoidPackageManifest,
                cancellationToken);
            var results = new List<VerifierCheckResultRecord>();
            results.AddRange(await CheckVoidManifestAsync(request.PackagePath, manifest, cancellationToken));
            if (results.Any(x => x.Status == VerificationCheckStatus.Fail))
            {
                var failedOverall = CalculateOverallStatus(results);
                var failedOutput = CreateOutput(
                    manifest.PackageId.ToString(),
                    manifest.ElectionId,
                    request.VerifierProfileId,
                    failedOverall,
                    results);
                return await WriteOutputAsync(request, failedOutput, cancellationToken);
            }

            var status = await ReadJsonAsync<VoidPublicStatusRecord>(
                request.PackagePath,
                VerificationPackageFileNames.VoidPublicStatus,
                cancellationToken);
            var decision = await ReadJsonAsync<VoidDecisionRecord>(
                request.PackagePath,
                VerificationPackageFileNames.VoidDecision,
                cancellationToken);

            results.Add(CheckVoidElectionConsistency(manifest, status, decision));
            results.Add(CheckVoidTerminalResult(manifest, status, decision));

            var overall = CalculateOverallStatus(results);
            var output = CreateOutput(
                manifest.PackageId.ToString(),
                manifest.ElectionId,
                request.VerifierProfileId,
                overall,
                results);
            return await WriteOutputAsync(request, output, cancellationToken);
        }
        catch (JsonException exception)
        {
            return await WriteUnparseableOutputAsync(request, exception.Message, cancellationToken);
        }
        catch (FormatException exception)
        {
            return await WriteUnparseableOutputAsync(request, exception.Message, cancellationToken);
        }
        catch (IOException exception)
        {
            var output = CreateOutput(
                packageId: "void-unreadable",
                electionId: "void-unreadable",
                request.VerifierProfileId,
                VerificationOverallStatus.NotAvailable,
                [
                    CreateResult(
                        "VFY-VOID-IO",
                        VerificationCheckStatus.Fail,
                        VerificationResultCodes.PackageUnreadable,
                        exception.Message),
                ]);
            return await WriteOutputAsync(request, output, cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<VerifierCheckResultRecord>> CheckVoidManifestAsync(
        string packagePath,
        VoidPackageManifestRecord manifest,
        CancellationToken cancellationToken)
    {
        var results = new List<VerifierCheckResultRecord>();
        var requiredFiles = VerificationPackageFileNames.VoidPublicFiles
            .Where(x => x != VerificationPackageFileNames.VoidPackageArchive)
            .ToArray();
        foreach (var requiredFile in requiredFiles)
        {
            if (!File.Exists(ResolvePackagePath(packagePath, requiredFile)))
            {
                results.Add(CreateResult(
                    "VFY-VOID-MAN-001",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.PackageManifestMissingArtifact,
                    $"Required VOID package file '{requiredFile}' is missing."));
            }
        }

        foreach (var entry in manifest.Entries)
        {
            var fullPath = ResolvePackagePath(packagePath, entry.Path);
            if (!File.Exists(fullPath))
            {
                results.Add(CreateResult(
                    "VFY-VOID-MAN-002",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.PackageManifestMissingArtifact,
                    $"VOID manifest entry '{entry.Path}' is missing."));
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            var actual = $"sha256:{VerificationCanonicalHash.ComputeManifestFileSha256(bytes)}";
            if (!string.Equals(actual, entry.Sha256Hash, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(CreateResult(
                    "VFY-VOID-MAN-003",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.PackageManifestArtifactHashMismatch,
                    $"VOID manifest entry '{entry.Path}' hash does not match exported bytes.",
                    new Dictionary<string, string>
                    {
                        ["expected"] = entry.Sha256Hash,
                        ["actual"] = actual,
                    }));
            }
        }

        if (results.Count == 0)
        {
            results.Add(CreateResult(
                "VFY-VOID-MAN-000",
                VerificationCheckStatus.Pass,
                VerificationResultCodes.PackageManifestValid,
                "VOID package manifest entries exist and match their SHA-256 hashes."));
        }

        return results;
    }

    private static VerifierCheckResultRecord CheckVoidElectionConsistency(
        VoidPackageManifestRecord manifest,
        VoidPublicStatusRecord status,
        VoidDecisionRecord decision)
    {
        if (!string.Equals(manifest.ElectionId, status.ElectionId, StringComparison.Ordinal) ||
            !string.Equals(manifest.ElectionId, decision.ElectionId, StringComparison.Ordinal) ||
            manifest.VoidDecisionId != status.VoidDecisionId ||
            manifest.VoidDecisionId != decision.VoidDecisionId ||
            manifest.PublicationAttemptId != status.PublicationAttemptId)
        {
            return CreateResult(
                "VFY-VOID-ELECTION-001",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.ElectionIdMismatch,
                "VOID package election, decision, or publication-attempt ids differ across files.");
        }

        return CreateResult(
            "VFY-VOID-ELECTION-000",
            VerificationCheckStatus.Pass,
            VerificationResultCodes.PackageStructureValid,
            "VOID package election and decision ids are consistent.");
    }

    private static VerifierCheckResultRecord CheckVoidTerminalResult(
        VoidPackageManifestRecord manifest,
        VoidPublicStatusRecord status,
        VoidDecisionRecord decision)
    {
        if (!string.Equals(manifest.Status, "VOID", StringComparison.Ordinal) ||
            !string.Equals(status.Status, "VOID", StringComparison.Ordinal) ||
            !string.Equals(status.VerifierResultCode, VerificationResultCodes.ElectionVoided, StringComparison.Ordinal) ||
            !string.Equals(decision.ResultingLifecycleState, "Voided", StringComparison.Ordinal))
        {
            return CreateResult(
                "VFY-VOID-001",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PackageUnparseable,
                "VOID package does not carry the required terminal VOID markers.");
        }

        return CreateResult(
            "VFY-VOID-001",
            VerificationCheckStatus.Warn,
            VerificationResultCodes.ElectionVoided,
            "This election is voided. No current final-result or final-inclusion claim is available.",
            new Dictionary<string, string>
            {
                ["void_decision_id"] = decision.VoidDecisionId.ToString(),
                ["previous_lifecycle_state"] = decision.PreviousLifecycleState,
                ["resulting_lifecycle_state"] = decision.ResultingLifecycleState,
            });
    }

    private sealed record VoidPackageManifestRecord(
        string SchemaId,
        Guid PackageId,
        string ElectionId,
        Guid VoidDecisionId,
        Guid PublicationAttemptId,
        string Status,
        string VerifierResultCode,
        string PackageHashCanonicalization,
        string PackageHash,
        DateTime CreatedAt,
        IReadOnlyList<VoidPackageManifestEntryRecord> Entries);

    private sealed record VoidPackageManifestEntryRecord(
        string Path,
        string Sha256Hash,
        string MediaType,
        string AccessScope,
        string ArtifactKind,
        string Format);

    private sealed record VoidPublicStatusRecord(
        string ElectionId,
        Guid VoidDecisionId,
        Guid PublicationAttemptId,
        string Status,
        string PublicJustification,
        string VerifierResultCode,
        string VoidPackageArtifactRef,
        string VoidPackageHash,
        DateTime PublishedAt);

    private sealed record VoidDecisionRecord(
        Guid VoidDecisionId,
        string ElectionId,
        string ActorPublicAddress,
        string ActorRole,
        Guid? SourceTransactionId,
        long? SourceBlockHeight,
        Guid? SourceBlockId,
        DateTime DecidedAt,
        string PreviousLifecycleState,
        string ResultingLifecycleState,
        string PublicStatus,
        string PublicJustification,
        string PublicJustificationHash);
}
