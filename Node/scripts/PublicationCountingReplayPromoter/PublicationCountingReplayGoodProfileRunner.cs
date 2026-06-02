using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HushShared.Elections.Verification.Model;

namespace PublicationCountingReplayPromoter;

public sealed record PublicationCountingReplayArtifactBinding(
    string BindingType,
    string Path,
    string Sha256Hash);

public sealed record PublicationCountingGoodProfileReplayCase(
    string FixtureId,
    string Status,
    string ProfileId,
    string PackagePath,
    string PackageHash,
    string ObservedPackageHash,
    string LocalDirectoryHash,
    string PackageHashStatus,
    string ExpectedResultRef,
    string ExpectedOverallStatus,
    string ObservedOverallStatus,
    int ExpectedExitCode,
    int ObservedExitCode,
    string ExpectedPrimaryResultCode,
    string ObservedPrimaryResultCode,
    string ExpectedNormalizedOutputHash,
    string NormalizedOutputHash,
    string NormalizedOutputHashStatus,
    IReadOnlyList<PublicationCountingReplayArtifactBinding> ArtifactBindings,
    IReadOnlyList<string> WarningsAffectingAuditConfidence,
    IReadOnlyList<string> MismatchReasons)
{
    public bool Passed =>
        string.Equals(Status, "matched", StringComparison.Ordinal) &&
        MismatchReasons.Count == 0 &&
        WarningsAffectingAuditConfidence.Count == 0;
}

public sealed record PublicationCountingGoodProfileReplaySet(
    string Status,
    IReadOnlyList<PublicationCountingGoodProfileReplayCase> Cases,
    IReadOnlyList<string> BlockingReasons)
{
    public int CaseCount => Cases.Count;

    public int PassCount => Cases.Count(item => item.Passed);

    public int FailCount => CaseCount - PassCount;

    public bool Passed =>
        string.Equals(Status, "pass", StringComparison.Ordinal) &&
        BlockingReasons.Count == 0 &&
        FailCount == 0;
}

public interface IPublicationCountingReplayProfileRunner
{
    PublicationCountingGoodProfileReplaySet ReplayGoodProfiles(
        PublicationCountingReplayPromotionPaths paths,
        JsonObject source);
}

public sealed class PublicationCountingReplayProfileRunner : IPublicationCountingReplayProfileRunner
{
    private const string PackageHashBinding = "package-hash";
    private const string TallyOutputBinding = "tally-output";
    private const string RuntimeVerifierOutputBinding = "runtime-verifier-output";
    private const string PackageHashMissing = "sha256:0000000000000000000000000000000000000000000000000000000000000000";

    public PublicationCountingGoodProfileReplaySet ReplayGoodProfiles(
        PublicationCountingReplayPromotionPaths paths,
        JsonObject source)
    {
        var cases = GoodProfiles(source)
            .Select(profile => ReplayProfile(paths, profile))
            .ToArray();
        var blockers = cases
            .SelectMany(item => item.MismatchReasons
                .Concat(item.WarningsAffectingAuditConfidence)
                .Select(reason => $"{item.FixtureId}: {reason}"))
            .ToArray();

        return new PublicationCountingGoodProfileReplaySet(
            blockers.Length == 0 ? "pass" : "blocked",
            cases,
            blockers);
    }

    private static PublicationCountingGoodProfileReplayCase ReplayProfile(
        PublicationCountingReplayPromotionPaths paths,
        JsonObject profile)
    {
        var fixtureId = PublicationCountingReplayContracts.GetString(profile, "fixtureId");
        var packagePathText = PublicationCountingReplayContracts.GetString(profile, "packagePath");
        var packagePath = PublicationCountingReplayContracts.ResolveWorkspaceRelativePath(paths.WorkspaceRoot, packagePathText);
        var expectedResultRef = PublicationCountingReplayContracts.GetString(profile, "expectedResultRef");
        var expectedResultPath = PublicationCountingReplayContracts.ResolveWorkspaceRelativePath(paths.WorkspaceRoot, expectedResultRef);
        var expectedResult = PublicationCountingReplayContracts.ReadJsonObject(expectedResultPath, $"{fixtureId} expected result");
        var profileId = PublicationCountingReplayContracts.GetString(expectedResult, "profileId", VerificationProfileIds.PublicAnonymousV1);
        var expectedOverallStatus = PublicationCountingReplayContracts.GetString(profile, "expectedOverallStatus");
        var expectedExitCode = PublicationCountingReplayContracts.GetInt(profile, "expectedExitCode");
        var expectedPrimaryResultCode = PublicationCountingReplayContracts.GetString(profile, "expectedPrimaryResultCode");
        var expectedNormalizedOutputHash = PublicationCountingReplayContracts.GetString(profile, "normalizedOutputHash");
        var expectedPackageHash = PublicationCountingReplayContracts.GetString(profile, "packageHash");
        var acceptedPackageHash = ResolveExpectedResultPackageHash(expectedResult, expectedPackageHash);
        var localDirectoryHash = Directory.Exists(packagePath)
            ? ComputeDirectoryHash(packagePath)
            : PackageHashMissing;
        var packageArtifactBindings = BuildPackageArtifactBindings(packagePath, acceptedPackageHash, localDirectoryHash);
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "hush-feat160-good-profile-replay",
            Guid.NewGuid().ToString("N"),
            fixtureId);

        try
        {
            var verification = new HushVotingPackageVerifier().VerifyAsync(
                    new HushVotingPackageVerificationRequest(packagePath, profileId, outputDirectory))
                .GetAwaiter()
                .GetResult();
            var observedOverallStatus = ToJsonEnumString(verification.Output.OverallStatus);
            var observedPrimaryResultCode = ResolveObservedPrimaryResultCode(
                verification.Output,
                expectedPrimaryResultCode);
            var normalizedOutputHash = Sha256Text(CanonicalJson(NormalizeVerifierOutput(verification.Output)));
            var artifactBindings = packageArtifactBindings
                .Concat([
                    new PublicationCountingReplayArtifactBinding(
                        RuntimeVerifierOutputBinding,
                        "normalized-verifier-output",
                        normalizedOutputHash),
                ])
                .ToArray();
            var warnings = verification.Output.Results
                .Where(item => item.Status == VerificationCheckStatus.Warn)
                .Select(item => $"{item.CheckCode}:{item.ResultCode}:{item.Message}")
                .ToArray();
            var mismatchReasons = BuildMismatchReasons(
                expectedPackageHash,
                acceptedPackageHash,
                expectedOverallStatus,
                observedOverallStatus,
                expectedExitCode,
                verification.ExitCode,
                expectedPrimaryResultCode,
                observedPrimaryResultCode,
                expectedNormalizedOutputHash,
                normalizedOutputHash,
                artifactBindings);

            return new PublicationCountingGoodProfileReplayCase(
                fixtureId,
                mismatchReasons.Count == 0 && warnings.Length == 0 ? "matched" : "mismatch",
                profileId,
                packagePathText,
                expectedPackageHash,
                acceptedPackageHash,
                localDirectoryHash,
                string.Equals(expectedPackageHash, acceptedPackageHash, StringComparison.OrdinalIgnoreCase)
                    ? "matched"
                    : "mismatch",
                expectedResultRef,
                expectedOverallStatus,
                observedOverallStatus,
                expectedExitCode,
                verification.ExitCode,
                expectedPrimaryResultCode,
                observedPrimaryResultCode,
                expectedNormalizedOutputHash,
                normalizedOutputHash,
                string.Equals(expectedNormalizedOutputHash, normalizedOutputHash, StringComparison.OrdinalIgnoreCase)
                    ? "matched"
                    : "mismatch",
                artifactBindings,
                warnings,
                mismatchReasons);
        }
        finally
        {
            TryDeleteDirectory(outputDirectory);
        }
    }

    private static IReadOnlyList<string> BuildMismatchReasons(
        string expectedPackageHash,
        string observedPackageHash,
        string expectedOverallStatus,
        string observedOverallStatus,
        int expectedExitCode,
        int observedExitCode,
        string expectedPrimaryResultCode,
        string observedPrimaryResultCode,
        string expectedNormalizedOutputHash,
        string normalizedOutputHash,
        IReadOnlyList<PublicationCountingReplayArtifactBinding> artifactBindings)
    {
        var reasons = new List<string>();
        AddMismatch(reasons, "package-hash", expectedPackageHash, observedPackageHash, ignoreCase: true);
        AddMismatch(reasons, "overall-status", expectedOverallStatus, observedOverallStatus, ignoreCase: false);
        if (expectedExitCode != observedExitCode)
        {
            reasons.Add($"exit-code expected {expectedExitCode}, observed {observedExitCode}");
        }

        AddMismatch(reasons, "primary-result-code", expectedPrimaryResultCode, observedPrimaryResultCode, ignoreCase: false);
        AddMismatch(reasons, "normalized-output-hash", expectedNormalizedOutputHash, normalizedOutputHash, ignoreCase: true);
        foreach (var binding in artifactBindings.Where(item => string.Equals(item.Sha256Hash, PackageHashMissing, StringComparison.Ordinal)))
        {
            reasons.Add($"{binding.BindingType} binding is missing required artifact {binding.Path}");
        }

        return reasons;
    }

    private static void AddMismatch(
        List<string> reasons,
        string label,
        string expected,
        string observed,
        bool ignoreCase)
    {
        if (!string.Equals(
                expected,
                observed,
                ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            reasons.Add($"{label} expected {expected}, observed {observed}");
        }
    }

    private static string ResolveObservedPrimaryResultCode(
        VerifierOutputRecord output,
        string expectedPrimaryResultCode)
    {
        var expectedPass = output.Results.FirstOrDefault(item =>
            string.Equals(item.ResultCode, expectedPrimaryResultCode, StringComparison.Ordinal) &&
            item.Status == VerificationCheckStatus.Pass);
        if (expectedPass is not null)
        {
            return expectedPass.ResultCode;
        }

        return output.Results.FirstOrDefault()?.ResultCode ?? string.Empty;
    }

    private static string ResolveExpectedResultPackageHash(
        JsonObject expectedResult,
        string fallback)
    {
        if (expectedResult.TryGetPropertyValue("stableOutputExcerpt", out var node) &&
            node is JsonObject excerpt)
        {
            var packageHash = PublicationCountingReplayContracts.GetString(excerpt, "packageHash");
            if (!string.IsNullOrWhiteSpace(packageHash))
            {
                return packageHash;
            }
        }

        return fallback;
    }

    private static IReadOnlyList<PublicationCountingReplayArtifactBinding> BuildPackageArtifactBindings(
        string packagePath,
        string packageHash,
        string localDirectoryHash) =>
        [
            new(PackageHashBinding, ".", packageHash),
            new("local-directory-hash-diagnostic", ".", localDirectoryHash),
            HashPackageArtifact(TallyOutputBinding, packagePath, VerificationPackageFileNames.TallyReplay),
            HashPackageArtifact("package-verifier-output", packagePath, VerificationPackageFileNames.Sp07PublicationProofVerifierOutput),
            HashPackageArtifact("package-verifier-output", packagePath, VerificationPackageFileNames.Sp06TrusteeVerifierOutput),
            HashPackageArtifact("result-binding", packagePath, VerificationPackageFileNames.ResultBinding),
        ];

    private static PublicationCountingReplayArtifactBinding HashPackageArtifact(
        string bindingType,
        string packagePath,
        string relativePath)
    {
        var path = Path.Combine(packagePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return new PublicationCountingReplayArtifactBinding(
            bindingType,
            relativePath,
            File.Exists(path) ? Sha256PackageFile(path) : PackageHashMissing);
    }

    private static JsonObject NormalizeVerifierOutput(VerifierOutputRecord output)
    {
        var results = new JsonArray();
        foreach (var result in output.Results
                     .OrderBy(item => item.CheckCode, StringComparer.Ordinal)
                     .ThenBy(item => item.ResultCode, StringComparer.Ordinal))
        {
            results.Add(new JsonObject
            {
                ["checkCode"] = result.CheckCode,
                ["status"] = ToJsonEnumString(result.Status),
                ["resultCode"] = result.ResultCode,
                ["message"] = result.Message,
            });
        }

        return new JsonObject
        {
            ["outputVersion"] = output.OutputVersion,
            ["packageId"] = output.PackageId,
            ["electionId"] = output.ElectionId,
            ["verifierProfileId"] = output.VerifierProfileId,
            ["overallStatus"] = ToJsonEnumString(output.OverallStatus),
            ["exitCode"] = output.ExitCode,
            ["results"] = results,
        };
    }

    private static string ComputeDirectoryHash(string root)
    {
        var builder = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(path => Path.GetRelativePath(root, path).Replace('\\', '/'), StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
            var fileHash = Sha256PackageFile(file).Replace("sha256:", string.Empty, StringComparison.Ordinal);
            builder.Append(relativePath).Append('|').Append(fileHash).Append('\n');
        }

        return Sha256Text(builder.ToString());
    }

    private static string Sha256PackageFile(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase))
        {
            return Sha256Text(PublicationCountingReplayContracts.NormalizeLineEndings(File.ReadAllText(path, Encoding.UTF8)));
        }

        return "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    }

    private static string CanonicalJson(JsonNode node) =>
        PublicationCountingReplayContracts.NormalizeLineEndings(node.ToJsonString(VerificationJson.Options)) + "\n";

    private static string Sha256Text(string content) =>
        "sha256:" + PublicationCountingReplayContracts.Sha256Hex(content);

    private static string ToJsonEnumString<T>(T value)
        where T : struct, Enum =>
        JsonSerializer.Serialize(value, VerificationJson.Options).Trim('"');

    private static IEnumerable<JsonObject> GoodProfiles(JsonObject source) =>
        PublicationCountingReplayContracts.RequireArray(
                PublicationCountingReplayContracts.RequireObject(source, "replayMatrix"),
                "goodProfiles")
            .OfType<JsonObject>();

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            var root = Directory.GetParent(path)?.FullName;
            if (!string.IsNullOrWhiteSpace(root) &&
                Directory.Exists(root) &&
                root.StartsWith(Path.Combine(Path.GetTempPath(), "hush-feat160-good-profile-replay"), StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
