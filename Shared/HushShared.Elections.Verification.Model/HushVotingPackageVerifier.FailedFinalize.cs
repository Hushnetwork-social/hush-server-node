namespace HushShared.Elections.Verification.Model;

public sealed partial class HushVotingPackageVerifier
{
    private static async Task<HushVotingPackageVerificationResult> VerifyFailedFinalizePackageAsync(
        HushVotingPackageVerificationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var manifest = await ReadJsonAsync<FailedFinalizePackageManifestRecord>(
                request.PackagePath,
                VerificationPackageFileNames.FailedFinalizePackageManifest,
                cancellationToken);
            var results = new List<VerifierCheckResultRecord>();
            results.AddRange(await CheckFailedFinalizeManifestAsync(request.PackagePath, manifest, cancellationToken));
            if (results.Any(x => x.Status == VerificationCheckStatus.Fail))
            {
                var failedOutput = CreateOutput(
                    manifest.PackageId,
                    manifest.ElectionId,
                    request.VerifierProfileId,
                    CalculateOverallStatus(results),
                    results);
                return await WriteOutputAsync(request, failedOutput, cancellationToken);
            }

            var status = await ReadJsonAsync<FailedFinalizePublicStatusArtifactRecord>(
                request.PackagePath,
                VerificationPackageFileNames.FailedFinalizePublicStatus,
                cancellationToken);
            var verifierResult = await ReadJsonAsync<FailedFinalizeVerifierResultArtifactRecord>(
                request.PackagePath,
                VerificationPackageFileNames.FailedFinalizeVerifierResult,
                cancellationToken);

            results.Add(CheckFailedFinalizeConsistency(manifest, status, verifierResult));
            results.Add(CheckFailedFinalizeNoCleanResult(status, verifierResult));

            var output = CreateOutput(
                manifest.PackageId,
                manifest.ElectionId,
                request.VerifierProfileId,
                CalculateOverallStatus(results),
                results);
            return await WriteOutputAsync(request, output, cancellationToken);
        }
        catch (System.Text.Json.JsonException exception)
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
                packageId: "failed-finalize-unreadable",
                electionId: "failed-finalize-unreadable",
                request.VerifierProfileId,
                VerificationOverallStatus.NotAvailable,
                [
                    CreateResult(
                        "VFY-FAILED-FINALIZE-IO",
                        VerificationCheckStatus.Fail,
                        VerificationResultCodes.PackageUnreadable,
                        exception.Message),
                ]);
            return await WriteOutputAsync(request, output, cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<VerifierCheckResultRecord>> CheckFailedFinalizeManifestAsync(
        string packagePath,
        FailedFinalizePackageManifestRecord manifest,
        CancellationToken cancellationToken)
    {
        var results = new List<VerifierCheckResultRecord>();
        if (manifest.Entries is null)
        {
            results.Add(CreateResult(
                FailedFinalizeVerificationIds.ManifestInvalidCheckCode,
                VerificationCheckStatus.Fail,
                VerificationResultCodes.PackageManifestMissingArtifact,
                "Failed-finalize package manifest entries are missing."));
            return results;
        }

        var manifestEntryPaths = manifest.Entries
            .Select(x => x.Path)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);
        var requiredManifestEntries = VerificationPackageFileNames.FailedFinalizePublicFiles
            .Where(x => x != VerificationPackageFileNames.FailedFinalizePackageManifest)
            .ToArray();
        foreach (var requiredFile in VerificationPackageFileNames.FailedFinalizePublicFiles)
        {
            if (!File.Exists(ResolvePackagePath(packagePath, requiredFile)))
            {
                results.Add(CreateResult(
                    FailedFinalizeVerificationIds.ManifestInvalidCheckCode,
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.PackageManifestMissingArtifact,
                    $"Required failed-finalize package file '{requiredFile}' is missing."));
            }
        }

        foreach (var requiredManifestEntry in requiredManifestEntries)
        {
            if (!manifestEntryPaths.Contains(requiredManifestEntry))
            {
                results.Add(CreateResult(
                    FailedFinalizeVerificationIds.ManifestInvalidCheckCode,
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.PackageManifestMissingArtifact,
                    $"Required failed-finalize package file '{requiredManifestEntry}' is not listed in the manifest."));
            }
        }

        foreach (var entry in manifest.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Path))
            {
                results.Add(CreateResult(
                    FailedFinalizeVerificationIds.ManifestInvalidCheckCode,
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.PackageManifestMissingArtifact,
                    "Failed-finalize manifest contains an entry without a path."));
                continue;
            }

            var fullPath = ResolvePackagePath(packagePath, entry.Path);
            if (!File.Exists(fullPath))
            {
                results.Add(CreateResult(
                    FailedFinalizeVerificationIds.ManifestInvalidCheckCode,
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.PackageManifestMissingArtifact,
                    $"Failed-finalize manifest entry '{entry.Path}' is missing."));
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            var actual = $"sha256:{VerificationCanonicalHash.ComputeManifestFileSha256(bytes)}";
            if (!string.Equals(actual, entry.Sha256Hash, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(CreateResult(
                    FailedFinalizeVerificationIds.ManifestInvalidCheckCode,
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.PackageManifestArtifactHashMismatch,
                    $"Failed-finalize manifest entry '{entry.Path}' hash does not match exported bytes.",
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
                FailedFinalizeVerificationIds.ManifestValidCheckCode,
                VerificationCheckStatus.Pass,
                VerificationResultCodes.PackageManifestValid,
                "Failed-finalize package manifest entries exist and match their SHA-256 hashes."));
        }

        return results;
    }

    private static VerifierCheckResultRecord CheckFailedFinalizeConsistency(
        FailedFinalizePackageManifestRecord manifest,
        FailedFinalizePublicStatusArtifactRecord status,
        FailedFinalizeVerifierResultArtifactRecord verifierResult)
    {
        if (!string.Equals(manifest.SchemaId, FailedFinalizeVerificationIds.PackageManifestSchemaId, StringComparison.Ordinal) ||
            !string.Equals(status.SchemaId, FailedFinalizeVerificationIds.PublicStatusSchemaId, StringComparison.Ordinal) ||
            !string.Equals(verifierResult.SchemaId, FailedFinalizeVerificationIds.VerifierResultSchemaId, StringComparison.Ordinal) ||
            !string.Equals(manifest.ElectionId, status.ElectionId, StringComparison.Ordinal) ||
            !string.Equals(manifest.ElectionId, verifierResult.ElectionId, StringComparison.Ordinal) ||
            !string.Equals(manifest.Status, FailedFinalizeVerificationIds.PackageStatusAccepted, StringComparison.Ordinal) ||
            !string.Equals(status.PackageStatus, FailedFinalizeVerificationIds.PackageStatusAccepted, StringComparison.Ordinal) ||
            !string.Equals(manifest.OutcomeStatus, FailedFinalizeVerificationIds.OutcomeStatusFailedToFinalize, StringComparison.Ordinal) ||
            !string.Equals(status.OutcomeStatus, FailedFinalizeVerificationIds.OutcomeStatusFailedToFinalize, StringComparison.Ordinal) ||
            !string.Equals(verifierResult.OutcomeStatus, FailedFinalizeVerificationIds.OutcomeStatusFailedToFinalize, StringComparison.Ordinal) ||
            !string.Equals(manifest.VerifierResultCode, VerificationResultCodes.FailedFinalizeContinuityValid, StringComparison.Ordinal) ||
            !string.Equals(verifierResult.ResultCode, VerificationResultCodes.FailedFinalizeContinuityValid, StringComparison.Ordinal) ||
            status.ContainsRestrictedDetails)
        {
            return CreateResult(
                FailedFinalizeVerificationIds.ConsistencyInvalidCheckCode,
                VerificationCheckStatus.Fail,
                VerificationResultCodes.FailedFinalizeClaimMismatch,
                "Failed-finalize package status, verifier result, or public-safety fields are inconsistent.");
        }

        return CreateResult(
            FailedFinalizeVerificationIds.ConsistencyValidCheckCode,
            VerificationCheckStatus.Pass,
            VerificationResultCodes.PackageStructureValid,
            "Failed-finalize package status and verifier result are consistent.");
    }

    private static VerifierCheckResultRecord CheckFailedFinalizeNoCleanResult(
        FailedFinalizePublicStatusArtifactRecord status,
        FailedFinalizeVerifierResultArtifactRecord verifierResult)
    {
        var hasFailedFinalizeEvidence =
            verifierResult.MissingFinalizeEvidenceRefs?.Count > 0 ||
            verifierResult.ContinuityEvidenceRefs?.Count > 0 ||
            verifierResult.AvailableTrusteeAcknowledgementRefs?.Count > 0;
        if (verifierResult.CleanFinalization ||
            !string.Equals(verifierResult.FinalizationMode, FailedFinalizeVerificationIds.FinalizationModeFailedFinalization, StringComparison.Ordinal) ||
            verifierResult.OfficialResultArtifactPresent ||
            verifierResult.CleanFinalPackagePresent ||
            verifierResult.FinalizeBoundaryArtifactPresent)
        {
            return CreateResult(
                FailedFinalizeVerificationIds.NoCleanResultCheckCode,
                VerificationCheckStatus.Fail,
                VerificationResultCodes.FailedFinalizeCleanResultConflict,
                "Failed-finalize package cannot contain clean finalization, official result, clean package, or finalize-boundary claims.");
        }

        if (!hasFailedFinalizeEvidence)
        {
            return CreateResult(
                FailedFinalizeVerificationIds.NoCleanResultCheckCode,
                VerificationCheckStatus.Fail,
                VerificationResultCodes.FailedFinalizeEvidenceMissing,
                "Failed-finalize package must contain missing-finalize or continuity evidence references.");
        }

        return CreateResult(
            FailedFinalizeVerificationIds.NoCleanResultCheckCode,
            VerificationCheckStatus.Warn,
            VerificationResultCodes.FailedFinalizeContinuityValid,
            "Clean finalization could not be verified. No official result or clean final package is available.",
            new Dictionary<string, string>
            {
                ["outcome_status"] = status.OutcomeStatus,
                ["clean_finalization"] = verifierResult.CleanFinalization.ToString(),
                ["finalization_mode"] = verifierResult.FinalizationMode,
            });
    }
}
