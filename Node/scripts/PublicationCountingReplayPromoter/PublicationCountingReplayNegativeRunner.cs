using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HushShared.Elections.Verification.Model;

namespace PublicationCountingReplayPromoter;

public sealed record PublicationCountingNegativeReplayCase(
    string CaseId,
    string FixtureId,
    string Source,
    string CoverageArea,
    string Status,
    string ProfileId,
    string PackagePath,
    string PackageHash,
    string ChangedArtifactOrCondition,
    IReadOnlyList<string> ChangedArtifactRefs,
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
    bool BlocksScoreMovement,
    IReadOnlyList<string> MismatchReasons)
{
    public bool Passed =>
        string.Equals(Status, "matched", StringComparison.Ordinal) &&
        MismatchReasons.Count == 0;
}

public sealed record PublicationCountingNegativeReplaySet(
    string Status,
    IReadOnlyList<PublicationCountingNegativeReplayCase> Cases,
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

public interface IPublicationCountingReplayNegativeRunner
{
    PublicationCountingNegativeReplaySet ReplayNegativeCases(
        PublicationCountingReplayPromotionPaths paths,
        JsonObject source);
}

public sealed class PublicationCountingReplayNegativeRunner : IPublicationCountingReplayNegativeRunner
{
    private const string PackageHashMissing = "sha256:0000000000000000000000000000000000000000000000000000000000000000";
    private const string CorpusPrefix = "HushVoting-Verifier-Corpus/hushvoting-v1/v0.3.0/";

    public PublicationCountingNegativeReplaySet ReplayNegativeCases(
        PublicationCountingReplayPromotionPaths paths,
        JsonObject source)
    {
        var fixtureIndex = LoadFixtureIndex(paths, source);
        var cases = PublicationCountingReplayContracts.RequireArray(source, "negativeMatrix")
            .OfType<JsonObject>()
            .Select(item => ReplayNegativeCase(paths, source, fixtureIndex, item))
            .ToArray();
        var blockers = cases
            .SelectMany(item => item.MismatchReasons.Select(reason => $"{item.FixtureId}: {reason}"))
            .ToArray();

        return new PublicationCountingNegativeReplaySet(
            blockers.Length == 0 ? "pass" : "blocked",
            cases,
            blockers);
    }

    private static PublicationCountingNegativeReplayCase ReplayNegativeCase(
        PublicationCountingReplayPromotionPaths paths,
        JsonObject source,
        IReadOnlyDictionary<string, JsonObject> fixtureIndex,
        JsonObject negativeCase)
    {
        var sourceKind = PublicationCountingReplayContracts.GetString(negativeCase, "source");
        return string.Equals(sourceKind, "feat160_required", StringComparison.Ordinal)
            ? ReplayGeneratedTrusteeCase(paths, source, negativeCase)
            : ReplayExistingCorpusCase(paths, source, fixtureIndex, negativeCase);
    }

    private static PublicationCountingNegativeReplayCase ReplayExistingCorpusCase(
        PublicationCountingReplayPromotionPaths paths,
        JsonObject source,
        IReadOnlyDictionary<string, JsonObject> fixtureIndex,
        JsonObject negativeCase)
    {
        var fixtureId = PublicationCountingReplayContracts.GetString(negativeCase, "fixtureId");
        if (!fixtureIndex.TryGetValue(fixtureId, out var indexEntry))
        {
            return MissingFixtureCase(negativeCase, $"FEAT-158 fixture index missing {fixtureId}.");
        }

        var corpusRoot = ResolveCorpusRoot(paths, source);
        var fixtureManifestRef = PublicationCountingReplayContracts.GetString(indexEntry, "fixtureManifestRef");
        var fixtureManifest = PublicationCountingReplayContracts.ReadJsonObject(
            Path.Combine(corpusRoot, fixtureManifestRef.Replace('/', Path.DirectorySeparatorChar)),
            $"{fixtureId} fixture manifest");
        var expectedResultRef = PublicationCountingReplayContracts.GetString(fixtureManifest, "expectedOutputRef");
        var expectedResult = PublicationCountingReplayContracts.ReadJsonObject(
            Path.Combine(corpusRoot, expectedResultRef.Replace('/', Path.DirectorySeparatorChar)),
            $"{fixtureId} expected result");
        var packageRef = PublicationCountingReplayContracts.GetString(fixtureManifest, "packagePath");
        var verifyPackagePath = HushVotingPackageVerifier.IsLiveDependency(packageRef)
            ? packageRef
            : Path.Combine(corpusRoot, packageRef.Replace('/', Path.DirectorySeparatorChar));
        var publicPackagePath = HushVotingPackageVerifier.IsLiveDependency(packageRef)
            ? packageRef
            : CorpusPrefix + packageRef;

        return ReplayVerifierCase(
            negativeCase,
            profileId: PublicationCountingReplayContracts.GetString(fixtureManifest, "profileId", VerificationProfileIds.PublicAnonymousV1),
            verifyPackagePath,
            publicPackagePath,
            packageHash: ResolveExpectedPackageHash(expectedResult, PublicationCountingReplayContracts.GetString(indexEntry, "packageHash")),
            expectedResultRef: CorpusPrefix + expectedResultRef,
            expectedNormalizedOutputHash: PublicationCountingReplayContracts.GetString(expectedResult, "normalizedOutputHash"),
            changedArtifactRefs: [PublicationCountingReplayContracts.GetString(fixtureManifest, "changedArtifact")]);
    }

    private static PublicationCountingNegativeReplayCase ReplayGeneratedTrusteeCase(
        PublicationCountingReplayPromotionPaths paths,
        JsonObject source,
        JsonObject negativeCase)
    {
        var fixtureId = PublicationCountingReplayContracts.GetString(negativeCase, "fixtureId");
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "hush-feat160-negative-replay",
            Guid.NewGuid().ToString("N"));
        var generatedPackagePath = Path.Combine(tempRoot, fixtureId);
        var sourcePackagePath = RequiredGoodProfilePackagePath(paths, source, "sample-good-trustee-threshold");

        try
        {
            CopyDirectory(sourcePackagePath, generatedPackagePath);
            MutateTrusteeNegativePackage(generatedPackagePath, fixtureId);
            RefreshPackageRootManifests(generatedPackagePath);
            var packageHash = ComputeDirectoryHash(generatedPackagePath);
            return ReplayVerifierCase(
                negativeCase,
                profileId: VerificationProfileIds.PublicAnonymousV1,
                verifyPackagePath: generatedPackagePath,
                publicPackagePath: $"generated-from:{CorpusPrefix}packages/sample-good-trustee-threshold#{fixtureId}",
                packageHash,
                expectedResultRef: $"generated-feat160-required:{fixtureId}",
                expectedNormalizedOutputHash: string.Empty,
                changedArtifactRefs: [VerificationPackageFileNames.Sp06TrusteeControlSummary]);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static PublicationCountingNegativeReplayCase ReplayVerifierCase(
        JsonObject negativeCase,
        string profileId,
        string verifyPackagePath,
        string publicPackagePath,
        string packageHash,
        string expectedResultRef,
        string expectedNormalizedOutputHash,
        IReadOnlyList<string> changedArtifactRefs)
    {
        var fixtureId = PublicationCountingReplayContracts.GetString(negativeCase, "fixtureId");
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "hush-feat160-negative-output",
            Guid.NewGuid().ToString("N"),
            fixtureId);
        try
        {
            var verification = new HushVotingPackageVerifier().VerifyAsync(
                    new HushVotingPackageVerificationRequest(verifyPackagePath, profileId, outputDirectory))
                .GetAwaiter()
                .GetResult();
            var observedOverallStatus = ToJsonEnumString(verification.Output.OverallStatus);
            var observedPrimaryResultCode = ResolveObservedPrimaryResultCode(
                verification.Output,
                PublicationCountingReplayContracts.GetString(negativeCase, "expectedPrimaryResultCode"));
            var normalizedOutputHash = Sha256Text(CanonicalJson(NormalizeVerifierOutput(verification.Output)));
            var expectedHash = string.IsNullOrWhiteSpace(expectedNormalizedOutputHash)
                ? normalizedOutputHash
                : expectedNormalizedOutputHash;
            var mismatchReasons = BuildMismatchReasons(
                negativeCase,
                observedOverallStatus,
                verification.ExitCode,
                observedPrimaryResultCode,
                expectedHash,
                normalizedOutputHash,
                changedArtifactRefs);

            return new PublicationCountingNegativeReplayCase(
                PublicationCountingReplayContracts.GetString(negativeCase, "caseId"),
                fixtureId,
                PublicationCountingReplayContracts.GetString(negativeCase, "source"),
                PublicationCountingReplayContracts.GetString(negativeCase, "coverageArea"),
                mismatchReasons.Count == 0 ? "matched" : "mismatch",
                profileId,
                publicPackagePath,
                packageHash,
                PublicationCountingReplayContracts.GetString(negativeCase, "changedArtifactOrCondition"),
                changedArtifactRefs.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray(),
                expectedResultRef,
                PublicationCountingReplayContracts.GetString(negativeCase, "expectedOverallStatus"),
                observedOverallStatus,
                PublicationCountingReplayContracts.GetInt(negativeCase, "expectedExitCode"),
                verification.ExitCode,
                PublicationCountingReplayContracts.GetString(negativeCase, "expectedPrimaryResultCode"),
                observedPrimaryResultCode,
                expectedHash,
                normalizedOutputHash,
                string.Equals(expectedHash, normalizedOutputHash, StringComparison.OrdinalIgnoreCase)
                    ? "matched"
                    : "mismatch",
                PublicationCountingReplayContracts.GetBool(negativeCase, "blocksScoreMovement"),
                mismatchReasons);
        }
        finally
        {
            TryDeleteDirectory(outputDirectory);
        }
    }

    private static IReadOnlyList<string> BuildMismatchReasons(
        JsonObject negativeCase,
        string observedOverallStatus,
        int observedExitCode,
        string observedPrimaryResultCode,
        string expectedNormalizedOutputHash,
        string normalizedOutputHash,
        IReadOnlyList<string> changedArtifactRefs)
    {
        var reasons = new List<string>();
        AddMismatch(
            reasons,
            "overall-status",
            PublicationCountingReplayContracts.GetString(negativeCase, "expectedOverallStatus"),
            observedOverallStatus,
            ignoreCase: false);
        var expectedExitCode = PublicationCountingReplayContracts.GetInt(negativeCase, "expectedExitCode");
        if (expectedExitCode != observedExitCode)
        {
            reasons.Add($"exit-code expected {expectedExitCode}, observed {observedExitCode}");
        }

        AddMismatch(
            reasons,
            "primary-result-code",
            PublicationCountingReplayContracts.GetString(negativeCase, "expectedPrimaryResultCode"),
            observedPrimaryResultCode,
            ignoreCase: false);
        AddMismatch(
            reasons,
            "normalized-output-hash",
            expectedNormalizedOutputHash,
            normalizedOutputHash,
            ignoreCase: true);
        if (!PublicationCountingReplayContracts.GetBool(negativeCase, "blocksScoreMovement"))
        {
            reasons.Add("negative case does not block score movement.");
        }

        if (changedArtifactRefs.Count == 0 || changedArtifactRefs.Any(string.IsNullOrWhiteSpace))
        {
            reasons.Add("negative case is missing changed-artifact references.");
        }

        return reasons;
    }

    private static PublicationCountingNegativeReplayCase MissingFixtureCase(
        JsonObject negativeCase,
        string reason)
    {
        var fixtureId = PublicationCountingReplayContracts.GetString(negativeCase, "fixtureId");
        return new PublicationCountingNegativeReplayCase(
            PublicationCountingReplayContracts.GetString(negativeCase, "caseId"),
            fixtureId,
            PublicationCountingReplayContracts.GetString(negativeCase, "source"),
            PublicationCountingReplayContracts.GetString(negativeCase, "coverageArea"),
            "mismatch",
            VerificationProfileIds.PublicAnonymousV1,
            "missing",
            PackageHashMissing,
            PublicationCountingReplayContracts.GetString(negativeCase, "changedArtifactOrCondition"),
            [],
            "missing",
            PublicationCountingReplayContracts.GetString(negativeCase, "expectedOverallStatus"),
            "notAvailable",
            PublicationCountingReplayContracts.GetInt(negativeCase, "expectedExitCode"),
            VerificationExitCodes.UnreadableOrUnparseable,
            PublicationCountingReplayContracts.GetString(negativeCase, "expectedPrimaryResultCode"),
            "missing_fixture",
            PackageHashMissing,
            PackageHashMissing,
            "mismatch",
            PublicationCountingReplayContracts.GetBool(negativeCase, "blocksScoreMovement"),
            [reason]);
    }

    private static IReadOnlyDictionary<string, JsonObject> LoadFixtureIndex(
        PublicationCountingReplayPromotionPaths paths,
        JsonObject source)
    {
        var corpusRoot = ResolveCorpusRoot(paths, source);
        var fixtureIndexPath = Path.Combine(corpusRoot, "fixtures", "fixture-index.json");
        return PublicationCountingReplayContracts.RequireArray(
                PublicationCountingReplayContracts.ReadJsonObject(fixtureIndexPath, "FEAT-158 fixture index"),
                "fixtures")
            .OfType<JsonObject>()
            .ToDictionary(item => PublicationCountingReplayContracts.GetString(item, "fixtureId"), StringComparer.Ordinal);
    }

    private static string ResolveCorpusRoot(PublicationCountingReplayPromotionPaths paths, JsonObject source)
    {
        var feat158 = PublicationCountingReplayContracts.RequireObject(
            PublicationCountingReplayContracts.RequireObject(source, "upstreamBaselines"),
            "feat158");
        return PublicationCountingReplayContracts.ResolveWorkspaceRelativePath(
            paths.WorkspaceRoot,
            PublicationCountingReplayContracts.GetString(feat158, "corpusPath"));
    }

    private static string RequiredGoodProfilePackagePath(
        PublicationCountingReplayPromotionPaths paths,
        JsonObject source,
        string fixtureId)
    {
        var profile = PublicationCountingReplayContracts.RequireArray(
                PublicationCountingReplayContracts.RequireObject(source, "replayMatrix"),
                "goodProfiles")
            .OfType<JsonObject>()
            .Single(item => string.Equals(PublicationCountingReplayContracts.GetString(item, "fixtureId"), fixtureId, StringComparison.Ordinal));
        return PublicationCountingReplayContracts.ResolveWorkspaceRelativePath(
            paths.WorkspaceRoot,
            PublicationCountingReplayContracts.GetString(profile, "packagePath"));
    }

    private static string ResolveExpectedPackageHash(JsonObject expectedResult, string fallback)
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

    private static void MutateTrusteeNegativePackage(string packagePath, string fixtureId)
    {
        var summaryPath = Path.Combine(
            packagePath,
            VerificationPackageFileNames.Sp06TrusteeControlSummary.Replace('/', Path.DirectorySeparatorChar));
        var summary = JsonNode.Parse(File.ReadAllText(summaryPath, Encoding.UTF8))?.AsObject() ??
            throw new PublicationCountingReplayPromotionException("SP-06 trustee control summary is not a JSON object.");
        switch (fixtureId)
        {
            case "tamper-trustee-release-wrong-target":
                summary["rejectedReleaseArtifactCount"] =
                    PublicationCountingReplayContracts.GetInt(summary, "rejectedReleaseArtifactCount") + 1;
                var firstTrustee = summary["trustees"]!.AsArray()[0]!.AsObject();
                firstTrustee["releaseArtifactStatus"] = "Rejected";
                firstTrustee["failureCode"] = "WRONG_TARGET_SHARE";
                break;
            case "tamper-trustee-release-threshold-not-met":
                summary["acceptedReleaseArtifactCount"] = 2;
                break;
            default:
                throw new PublicationCountingReplayPromotionException(
                    "Unknown FEAT-160 trustee negative fixture.",
                    [$"Fixture: {fixtureId}"]);
        }

        File.WriteAllText(
            summaryPath,
            PublicationCountingReplayContracts.NormalizeLineEndings(summary.ToJsonString(VerificationJson.Options)),
            new UTF8Encoding(false));
    }

    private static void RefreshPackageRootManifests(string packagePath)
    {
        var manifestPath = Path.Combine(packagePath, VerificationPackageFileNames.AuditPackageManifest);
        var manifest = JsonSerializer.Deserialize<AuditPackageManifestRecord>(
            File.ReadAllText(manifestPath, Encoding.UTF8),
            VerificationJson.Options)!;
        var refreshedEntries = manifest.Entries
            .Select(entry =>
            {
                var path = Path.Combine(packagePath, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    return entry;
                }

                var bytes = File.ReadAllBytes(path);
                return entry with
                {
                    Sha256Hash = VerificationCanonicalHash.ComputeManifestFileSha256(bytes),
                    SizeBytes = bytes.Length,
                };
            })
            .ToArray();
        var refreshedManifest = manifest with { Entries = refreshedEntries };
        File.WriteAllText(
            manifestPath,
            PublicationCountingReplayContracts.NormalizeLineEndings(JsonSerializer.Serialize(refreshedManifest, VerificationJson.Options)),
            new UTF8Encoding(false));

        var inputManifestPath = Path.Combine(packagePath, VerificationPackageFileNames.VerifierInputManifest);
        var inputManifest = JsonSerializer.Deserialize<VerifierInputManifestRecord>(
            File.ReadAllText(inputManifestPath, Encoding.UTF8),
            VerificationJson.Options)!;
        var manifestBytes = File.ReadAllBytes(manifestPath);
        File.WriteAllText(
            inputManifestPath,
            PublicationCountingReplayContracts.NormalizeLineEndings(JsonSerializer.Serialize(
                inputManifest with
                {
                    AuditPackageManifestHash = VerificationCanonicalHash.ComputeManifestFileSha256(manifestBytes),
                },
                VerificationJson.Options)),
            new UTF8Encoding(false));
    }

    private static string ResolveObservedPrimaryResultCode(
        VerifierOutputRecord output,
        string expectedPrimaryResultCode)
    {
        var expected = output.Results.FirstOrDefault(item =>
            string.Equals(item.ResultCode, expectedPrimaryResultCode, StringComparison.Ordinal));
        if (expected is not null)
        {
            return expected.ResultCode;
        }

        return output.Results.FirstOrDefault(item => item.Status == VerificationCheckStatus.Fail)?.ResultCode ??
            output.Results.FirstOrDefault()?.ResultCode ??
            string.Empty;
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

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) &&
                path.StartsWith(Path.Combine(Path.GetTempPath(), "hush-feat160-"), StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(path, recursive: true);
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
