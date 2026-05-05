using System.Text.Json;

namespace HushShared.Elections.Verification.Model;

public record HushVotingPackageVerificationRequest(
    string PackagePath,
    string VerifierProfileId,
    string? OutputPath = null);

public record HushVotingPackageVerificationResult(
    VerifierOutputRecord Output,
    string SummaryMarkdown,
    int ExitCode);

public sealed partial class HushVotingPackageVerifier
{
    public async Task<HushVotingPackageVerificationResult> VerifyAsync(
        HushVotingPackageVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (IsLiveDependency(request.PackagePath))
        {
            var output = CreateOutput(
                packageId: "unavailable",
                electionId: "unavailable",
                request.VerifierProfileId,
                VerificationOverallStatus.Fail,
                [
                    CreateResult(
                        "VFY-CLI-001",
                        VerificationCheckStatus.Fail,
                        VerificationResultCodes.UnsupportedLiveDependency,
                        "The verifier only accepts local package paths in v1."),
                ]);
            return await WriteOutputAsync(request, output, cancellationToken);
        }

        if (!Directory.Exists(request.PackagePath))
        {
            var output = CreateOutput(
                packageId: "unavailable",
                electionId: "unavailable",
                request.VerifierProfileId,
                VerificationOverallStatus.NotAvailable,
                [
                    CreateResult(
                        "VFY-PKG-001",
                        VerificationCheckStatus.Fail,
                        VerificationResultCodes.PackageUnreadable,
                        "The package path does not exist or is not a directory."),
                ]);
            return await WriteOutputAsync(request, output, cancellationToken);
        }

        try
        {
            var manifest = await ReadJsonAsync<AuditPackageManifestRecord>(
                request.PackagePath,
                VerificationPackageFileNames.AuditPackageManifest,
                cancellationToken);
            var inputManifest = await ReadJsonAsync<VerifierInputManifestRecord>(
                request.PackagePath,
                VerificationPackageFileNames.VerifierInputManifest,
                cancellationToken);
            var profile = await ReadJsonAsync<VerifierProfileRecord>(
                request.PackagePath,
                VerificationPackageFileNames.VerifierProfile,
                cancellationToken);
            _ = await ReadJsonAsync<ElectionRecordReferenceRecord>(
                request.PackagePath,
                VerificationPackageFileNames.ElectionRecord,
                cancellationToken);

            var results = new List<VerifierCheckResultRecord>();
            var manifestResults = await CheckManifestAsync(request.PackagePath, manifest, cancellationToken);
            results.AddRange(manifestResults);
            results.Add(CheckProfile(request.VerifierProfileId, inputManifest, profile));

            if (manifestResults.Any(x => x.ResultCode == VerificationResultCodes.PackageManifestMissingArtifact))
            {
                var missingOutput = CreateOutput(
                    manifest.PackageId,
                    manifest.ElectionId,
                    request.VerifierProfileId,
                    VerificationOverallStatus.Fail,
                    results);
                return await WriteOutputAsync(request, missingOutput, cancellationToken);
            }

            results.Add(await CheckAcceptedBallotsAsync(request.PackagePath, cancellationToken));
            results.Add(await CheckPublishedBallotsAsync(request.PackagePath, cancellationToken));
            results.AddRange(await CheckPrivacyBoundaryAsync(request.PackagePath, manifest, cancellationToken));
            results.Add(await CheckFuturePublicationProofAsync(request.PackagePath, request.VerifierProfileId, cancellationToken));

            var overall = CalculateOverallStatus(results);
            var output = CreateOutput(
                manifest.PackageId,
                manifest.ElectionId,
                request.VerifierProfileId,
                overall,
                results);

            return await WriteOutputAsync(request, output, cancellationToken);
        }
        catch (JsonException exception)
        {
            var output = CreateOutput(
                packageId: "unparseable",
                electionId: "unparseable",
                request.VerifierProfileId,
                VerificationOverallStatus.NotAvailable,
                [
                    CreateResult(
                        "VFY-PKG-002",
                        VerificationCheckStatus.Fail,
                        VerificationResultCodes.PackageUnparseable,
                        exception.Message),
                ]);
            return await WriteOutputAsync(request, output, cancellationToken);
        }
        catch (IOException exception)
        {
            var output = CreateOutput(
                packageId: "unreadable",
                electionId: "unreadable",
                request.VerifierProfileId,
                VerificationOverallStatus.NotAvailable,
                [
                    CreateResult(
                        "VFY-PKG-003",
                        VerificationCheckStatus.Fail,
                        VerificationResultCodes.PackageUnreadable,
                        exception.Message),
                ]);
            return await WriteOutputAsync(request, output, cancellationToken);
        }
    }

    public static bool IsLiveDependency(string? packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            return false;
        }

        return Uri.TryCreate(packagePath, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" or "postgres" or "sqlserver";
    }

    private static VerificationOverallStatus CalculateOverallStatus(
        IReadOnlyList<VerifierCheckResultRecord> results)
    {
        if (results.Any(x => x.Status == VerificationCheckStatus.Fail))
        {
            return VerificationOverallStatus.Fail;
        }

        if (results.Any(x => x.Status == VerificationCheckStatus.Warn))
        {
            return VerificationOverallStatus.Warn;
        }

        return VerificationOverallStatus.Pass;
    }

    private static VerifierOutputRecord CreateOutput(
        string packageId,
        string electionId,
        string profileId,
        VerificationOverallStatus status,
        IReadOnlyList<VerifierCheckResultRecord> results) =>
        new(
            OutputVersion: "1.0",
            packageId,
            electionId,
            profileId,
            status,
            VerificationExitCodes.FromOverallStatus(status),
            DateTime.UtcNow,
            results);

    private static VerifierCheckResultRecord CreateResult(
        string checkCode,
        VerificationCheckStatus status,
        string resultCode,
        string message,
        IReadOnlyDictionary<string, string>? evidence = null) =>
        new(
            checkCode,
            status,
            resultCode,
            message,
            evidence ?? new Dictionary<string, string>());

}
