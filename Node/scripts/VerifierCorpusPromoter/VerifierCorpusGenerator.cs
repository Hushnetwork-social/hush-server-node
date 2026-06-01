using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HushNode.Elections;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;

namespace VerifierCorpusPromoter;

public sealed record VerifierCorpusGenerationOptions(
    string OutputRoot,
    string CorpusVersion,
    DateTimeOffset GeneratedAt,
    string PublicRepository = "https://github.com/Hushnetwork-social/HushVoting-Verifier-Corpus",
    string PublicRepositoryRef = "local-generated",
    string VerifierRepository = "https://github.com/Hushnetwork-social/hush-server-node",
    string VerifierSourceRef = "local-working-tree",
    string VerifierProjectPath = "Tools/HushVotingVerifier/HushVotingVerifier.csproj",
    string VerifierHash = "sha256:not-computed",
    bool WindowsReviewerReplayValidated = false,
    bool LinuxReviewerReplayValidated = false,
    string CorpusFamily = VerifierCorpusGenerator.DefaultCorpusFamily,
    string RepositoryRelativePath = "");

public sealed record VerifierCorpusFixtureGenerationResult(
    string FixtureId,
    string PackagePath,
    string VerifierProfileId,
    string CorpusProfileId,
    string ProfileDescription,
    string ExpectedPrimaryResultCode,
    VerificationCheckStatus ExpectedCheckStatus,
    VerificationOverallStatus ExpectedOverallStatus,
    int ExpectedExitCode,
    string PackageHash,
    string NormalizedOutputHash,
    bool SecondaryFailuresAllowed);

public sealed record VerifierCorpusGenerationResult(
    string OutputRoot,
    IReadOnlyList<VerifierCorpusFixtureGenerationResult> Fixtures,
    string ManifestHash,
    string FixtureIndexHash,
    string NoSecretScanStatus,
    IReadOnlyList<VerifierCorpusScanFinding> ScanFindings)
{
    public VerifierCorpusFixtureGenerationResult GoodSample =>
        Fixtures.Single(x => string.Equals(x.FixtureId, VerifierCorpusGenerator.GoodSampleFixtureId, StringComparison.Ordinal));
}

public sealed record VerifierCorpusScanFinding(
    string RelativePath,
    string Category,
    string Evidence,
    bool ExpectedTamperFixture);

public static class VerifierCorpusReadinessEvaluator
{
    public static bool GoodSampleBlocksAcceptance(VerifierCorpusFixtureGenerationResult goodSample) =>
        goodSample.ExpectedOverallStatus != VerificationOverallStatus.Pass || goodSample.ExpectedExitCode != VerificationExitCodes.Pass;
}

public sealed partial class VerifierCorpusGenerator
{
    public const string GoodSampleFixtureId = "sample-good-finalized-election";
    public const string DefaultCorpusFamily = "hushvoting-v1";

    private static readonly JsonSerializerOptions JsonOptions = VerificationJson.Options;

    private static readonly FixtureSpec[] LegacyFixtureSpecs =
    [
        new(GoodSampleFixtureId, "baseline_finalized", "Baseline finalized organizational election from FEAT-135.", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.PackageStructureValid, VerificationCheckStatus.Pass, VerificationOverallStatus.Pass, VerificationExitCodes.Pass, SecondaryFailuresAllowed: false),
        new("tamper-missing-artifact", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.PackageManifestMissingArtifact, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-artifact-hash", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.PackageManifestArtifactHashMismatch, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-malformed-package-json", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.PackageUnparseable, VerificationCheckStatus.Fail, VerificationOverallStatus.NotAvailable, VerificationExitCodes.UnreadableOrUnparseable),
        new("tamper-profile-mismatch", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.VerifierProfilePackageMismatch, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-unsupported-live-dependency", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.UnsupportedLiveDependency, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-wrong-election-id", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.ElectionIdMismatch, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-duplicate-nullifier", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.AcceptedBallotDuplicateNullifier, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-accepted-set-hash", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.AcceptedBallotInventoryHashMismatch, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-published-stream-sequence", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.PublishedBallotSequenceInvalid, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-published-stream-hash", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.PublishedBallotStreamHashMismatch, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-sp04-receipt-set-hash", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.ChallengeSpoilReceiptMismatch, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-sp04-count", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.ChallengeSpoilCountMismatch, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-sp04-accepted-binding", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.ChallengeSpoilReceiptMismatch, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-sp05-public-named-field", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.EligibilityPublicPrivacyBoundaryViolation, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-sp05-count-reconciliation", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.EligibilityCountReconciliationMismatch, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-sp08-release-manifest-hash", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.ReleaseIntegrityManifestMissing, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-sp08-mutable-artifact-reference", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.ReleaseIntegrityMutableArtifactReference, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-sp08-component-hash-mismatch", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.ReleaseIntegrityComponentHashMismatch, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-sp08-protocol-package-mismatch", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.ReleaseIntegrityCircuitOrPackageHashMismatch, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-sp08-circuit-key-hash-mismatch", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.ReleaseIntegrityCircuitOrPackageHashMismatch, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-sp10-forbidden-leak", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.OperationalSecurityForbiddenMaterial, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-sp10-kms-public-value-leak", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.OperationalSecurityForbiddenMaterial, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
    ];

    private static readonly FixtureSpec[] RefreshOnlyFixtureSpecs =
    [
        new("sample-good-larger-electorate", "larger_electorate", "Synthetic good sample with a larger roster, larger accepted-ballot set, and reconciled low non-voter count.", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.PackageStructureValid, VerificationCheckStatus.Pass, VerificationOverallStatus.Pass, VerificationExitCodes.Pass, SecondaryFailuresAllowed: false),
        new("sample-good-multi-option-single-winner", "multi_option_single_winner", "Synthetic good sample with a three-option single-winner package shape and reconciled verification artifacts.", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.PackageStructureValid, VerificationCheckStatus.Pass, VerificationOverallStatus.Pass, VerificationExitCodes.Pass, SecondaryFailuresAllowed: false),
        new("sample-good-trustee-threshold", "trustee_threshold", "Synthetic good sample with trustee-threshold finalization, accepted control-domain evidence, and release artifacts.", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.PackageStructureValid, VerificationCheckStatus.Pass, VerificationOverallStatus.Pass, VerificationExitCodes.Pass, SecondaryFailuresAllowed: false),
        new("sample-good-low-turnout", "low_turnout", "Synthetic good sample with a larger active denominator than counted participation.", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.PackageStructureValid, VerificationCheckStatus.Pass, VerificationOverallStatus.Pass, VerificationExitCodes.Pass, SecondaryFailuresAllowed: false),
        new("tamper-stale-verifier-source-ref", "stale_version_drift", "Stale verifier source reference must block score movement.", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.ReleaseIntegrityComponentHashMismatch, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-stale-verifier-binary-hash", "stale_version_drift", "Stale verifier binary hash must block score movement.", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.ReleaseIntegrityComponentHashMismatch, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-stale-protocol-package-version", "stale_version_drift", "Stale Protocol Omega package binding must block score movement.", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.ReleaseIntegrityCircuitOrPackageHashMismatch, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-package-schema-version-drift", "stale_version_drift", "Verification package schema drift must block score movement.", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.PackageUnparseable, VerificationCheckStatus.Fail, VerificationOverallStatus.NotAvailable, VerificationExitCodes.UnreadableOrUnparseable),
        new("tamper-expected-result-drift", "stale_version_drift", "Expected-result drift is represented as package manifest hash drift.", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.PackageManifestArtifactHashMismatch, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
        new("tamper-corpus-index-drift", "stale_version_drift", "Corpus index drift is represented as profile/package mismatch.", VerificationProfileIds.PublicAnonymousV1, VerificationResultCodes.VerifierProfilePackageMismatch, VerificationCheckStatus.Fail, VerificationOverallStatus.Fail, VerificationExitCodes.Fail),
    ];

    public async Task<VerifierCorpusGenerationResult> GenerateAsync(
        VerifierCorpusGenerationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        ResetGeneratedOutputTree(options.OutputRoot);

        var packagesRoot = Path.Combine(options.OutputRoot, "packages");
        var expectedResultsRoot = Path.Combine(options.OutputRoot, "expected-results");
        var verifierOutputRoot = Path.Combine(options.OutputRoot, "validation", "verifier-output");
        Directory.CreateDirectory(packagesRoot);
        Directory.CreateDirectory(expectedResultsRoot);
        Directory.CreateDirectory(verifierOutputRoot);

        var fixtureSpecs = ResolveFixtureSpecs(options.CorpusVersion);
        var goodPackagePath = Path.Combine(packagesRoot, GoodSampleFixtureId);
        await CreateGoodSamplePackageAsync(goodPackagePath, options.GeneratedAt, "baseline_finalized", cancellationToken);
        NormalizeTextFiles(goodPackagePath);
        await RefreshPackageRootManifestsAsync(goodPackagePath, cancellationToken);

        var results = new List<VerifierCorpusFixtureGenerationResult>();
        foreach (var spec in fixtureSpecs)
        {
            var packagePath = string.Equals(spec.FixtureId, GoodSampleFixtureId, StringComparison.Ordinal)
                ? goodPackagePath
                : Path.Combine(packagesRoot, spec.FixtureId);

            if (IsGoodSampleFixture(spec.FixtureId) &&
                !string.Equals(spec.FixtureId, GoodSampleFixtureId, StringComparison.Ordinal))
            {
                await CreateGoodSamplePackageAsync(packagePath, options.GeneratedAt, spec.CorpusProfileId, cancellationToken);
                NormalizeTextFiles(packagePath);
                await RefreshPackageRootManifestsAsync(packagePath, cancellationToken);
                await WriteProfileMarkerAsync(packagePath, spec, options.GeneratedAt, cancellationToken);
            }
            else if (!string.Equals(spec.FixtureId, GoodSampleFixtureId, StringComparison.Ordinal) &&
                !string.Equals(spec.FixtureId, "tamper-unsupported-live-dependency", StringComparison.Ordinal))
            {
                CopyDirectory(goodPackagePath, packagePath);
                await ApplyTamperAsync(packagePath, spec.FixtureId, cancellationToken);
            }

            if (Directory.Exists(packagePath))
            {
                NormalizeTextFiles(packagePath);
            }

            var verificationPackagePath = string.Equals(spec.FixtureId, "tamper-unsupported-live-dependency", StringComparison.Ordinal)
                ? "https://example.invalid/hushvoting-public-verifier-corpus/package"
                : packagePath;
            var verifierOutputPath = Path.Combine(verifierOutputRoot, spec.FixtureId);
            var verification = await new HushVotingPackageVerifier().VerifyAsync(
                new HushVotingPackageVerificationRequest(
                    verificationPackagePath,
                    spec.ProfileId,
                    verifierOutputPath),
                cancellationToken);
            NormalizeTextFiles(verifierOutputPath);

            ValidateVerifierResult(spec, verification);
            var packageHash = Directory.Exists(packagePath)
                ? ComputeDirectoryHash(packagePath)
                : "sha256:0000000000000000000000000000000000000000000000000000000000000000";
            var expectedResult = BuildExpectedResult(spec, verification, packageHash);
            var expectedResultPath = Path.Combine(expectedResultsRoot, $"{spec.FixtureId}.json");
            await WriteTextLfAsync(expectedResultPath, CanonicalJson(expectedResult), cancellationToken);

            results.Add(new VerifierCorpusFixtureGenerationResult(
                spec.FixtureId,
                verificationPackagePath,
                spec.ProfileId,
                spec.CorpusProfileId,
                spec.ProfileDescription,
                spec.ExpectedResultCode,
                spec.ExpectedCheckStatus,
                verification.Output.OverallStatus,
                verification.ExitCode,
                packageHash,
                expectedResult["normalizedOutputHash"]!.GetValue<string>(),
                spec.SecondaryFailuresAllowed));
        }

        var publicArtifacts = await RenderPublicArtifactsAsync(options, results, cancellationToken);
        return new VerifierCorpusGenerationResult(
            options.OutputRoot,
            results,
            publicArtifacts.ManifestHash,
            publicArtifacts.FixtureIndexHash,
            publicArtifacts.NoSecretScanStatus,
            publicArtifacts.ScanFindings);
    }

    public static IReadOnlyList<string> RequiredFixtureIds() =>
        LegacyFixtureSpecs.Select(x => x.FixtureId).ToArray();

    public static IReadOnlyList<string> RefreshFixtureIds() =>
        ResolveFixtureSpecs("v0.2.0").Select(x => x.FixtureId).ToArray();

    private static FixtureSpec[] ResolveFixtureSpecs(string corpusVersion) =>
        string.Equals(corpusVersion, "v0.2.0", StringComparison.OrdinalIgnoreCase)
            ? LegacyFixtureSpecs.Concat(RefreshOnlyFixtureSpecs).ToArray()
            : LegacyFixtureSpecs;

    private static bool IsGoodSampleFixture(string fixtureId) =>
        string.Equals(fixtureId, GoodSampleFixtureId, StringComparison.Ordinal) ||
        fixtureId.StartsWith("sample-good-", StringComparison.Ordinal);

    private static void ResetGeneratedOutputTree(string outputRoot)
    {
        var fullRoot = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(fullRoot);

        foreach (var relativePath in new[]
                 {
                     "packages",
                     "fixtures",
                     "expected-results",
                     "validation",
                     "readiness",
                     "handoff",
                 })
        {
            var path = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
            EnsurePathUnder(fullRoot, path, relativePath);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        foreach (var relativePath in new[] { "README.md", "corpus-manifest.json" })
        {
            var path = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
            EnsurePathUnder(fullRoot, path, relativePath);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static Task CreateGoodSamplePackageAsync(
        string packagePath,
        DateTimeOffset generatedAt,
        string corpusProfileId,
        CancellationToken cancellationToken)
    {
        var request = SyntheticElectionRequestFactory.CreatePublicAnonymousRequest(generatedAt, corpusProfileId);
        var export = new ElectionVerificationPackageExportService().Export(request);
        if (!export.Success)
        {
            throw new InvalidOperationException($"Synthetic public package export failed: {export.Code} {export.Message}");
        }

        ElectionVerificationPackageExportService.WritePackageToDirectory(export, packagePath);
        return Task.CompletedTask;
    }

    private static void ValidateVerifierResult(FixtureSpec spec, HushVotingPackageVerificationResult verification)
    {
        if (verification.Output.OverallStatus != spec.ExpectedOverallStatus)
        {
            throw new InvalidOperationException(
                $"{spec.FixtureId} returned overall status {verification.Output.OverallStatus}; expected {spec.ExpectedOverallStatus}. Results: {FormatResults(verification)}");
        }

        if (verification.ExitCode != spec.ExpectedExitCode)
        {
            throw new InvalidOperationException(
                $"{spec.FixtureId} returned exit code {verification.ExitCode}; expected {spec.ExpectedExitCode}. Results: {FormatResults(verification)}");
        }

        if (!verification.Output.Results.Any(x =>
                string.Equals(x.ResultCode, spec.ExpectedResultCode, StringComparison.Ordinal) &&
                x.Status == spec.ExpectedCheckStatus))
        {
            throw new InvalidOperationException(
                $"{spec.FixtureId} did not return expected result {spec.ExpectedResultCode}/{spec.ExpectedCheckStatus}. Results: {FormatResults(verification)}");
        }
    }

    private static string FormatResults(HushVotingPackageVerificationResult verification) =>
        string.Join(
            "; ",
            verification.Output.Results.Select(x => $"{x.CheckCode}:{x.Status}:{x.ResultCode}"));

    private static JsonObject BuildExpectedResult(
        FixtureSpec spec,
        HushVotingPackageVerificationResult verification,
        string packageHash)
    {
        var normalizedOutput = NormalizeVerifierOutput(verification.Output);
        var normalizedOutputText = CanonicalJson(normalizedOutput);
        var requiredStatuses = new JsonObject();
        foreach (var result in verification.Output.Results
                     .Where(x => string.Equals(x.ResultCode, spec.ExpectedResultCode, StringComparison.Ordinal)))
        {
            requiredStatuses[result.CheckCode] = ToJsonEnumString(result.Status);
        }

        return new JsonObject
        {
            ["schemaVersion"] = "verifier-corpus-expected-result.v1",
            ["fixtureId"] = spec.FixtureId,
            ["profileId"] = spec.ProfileId,
            ["expectedOverallStatus"] = ToJsonEnumString(verification.Output.OverallStatus),
            ["expectedExitCode"] = verification.ExitCode,
            ["expectedPrimaryResultCode"] = spec.ExpectedResultCode,
            ["requiredResultCodes"] = new JsonArray(spec.ExpectedResultCode),
            ["requiredCheckStatuses"] = requiredStatuses,
            ["ignoredFields"] = new JsonArray("verifiedAt", "outputPath", "absolutePackagePath"),
            ["stableOutputExcerpt"] = new JsonObject
            {
                ["packageId"] = verification.Output.PackageId,
                ["electionId"] = verification.Output.ElectionId,
                ["verifierProfileId"] = verification.Output.VerifierProfileId,
                ["overallStatus"] = ToJsonEnumString(verification.Output.OverallStatus),
                ["packageHash"] = packageHash,
            },
            ["normalizedOutputHash"] = Sha256Text(normalizedOutputText),
            ["outputRef"] = $"validation/verifier-output/{spec.FixtureId}/VerifierOutput.json",
        };
    }

    private static JsonObject NormalizeVerifierOutput(VerifierOutputRecord output)
    {
        var results = new JsonArray();
        foreach (var result in output.Results.OrderBy(x => x.CheckCode, StringComparer.Ordinal).ThenBy(x => x.ResultCode, StringComparer.Ordinal))
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

    private static async Task ApplyTamperAsync(
        string packagePath,
        string fixtureId,
        CancellationToken cancellationToken)
    {
        switch (fixtureId)
        {
            case "tamper-missing-artifact":
                File.Delete(ResolvePackagePath(packagePath, VerificationPackageFileNames.AcceptedBallotSet));
                return;

            case "tamper-artifact-hash":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.ResultBinding, root =>
                    root["tamperMarker"] = "artifact hash changed without updating the manifest", cancellationToken);
                return;

            case "tamper-malformed-package-json":
                await WriteTextLfAsync(
                    ResolvePackagePath(packagePath, VerificationPackageFileNames.ElectionRecord),
                    "{",
                    cancellationToken);
                return;

            case "tamper-profile-mismatch":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.VerifierProfile, root =>
                    root["profileId"] = VerificationProfileIds.RestrictedOwnerAuditorV1, cancellationToken);
                await RefreshPackageRootManifestsAsync(packagePath, cancellationToken);
                return;

            case "tamper-wrong-election-id":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.AcceptedBallotSet, root =>
                    root["electionId"] = "99999999-9999-9999-9999-999999999999", cancellationToken);
                await RefreshPackageRootManifestsAsync(packagePath, cancellationToken);
                return;

            case "tamper-duplicate-nullifier":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.AcceptedBallotSet, root =>
                {
                    var ballots = root["acceptedBallots"]!.AsArray();
                    ballots[1]!.AsObject()["ballotNullifier"] = ballots[0]!.AsObject()["ballotNullifier"]!.GetValue<string>();
                }, cancellationToken);
                return;

            case "tamper-accepted-set-hash":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.AcceptedBallotSet, root =>
                    root["acceptedBallotInventoryHash"] = new string('0', 64), cancellationToken);
                return;

            case "tamper-published-stream-sequence":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.PublishedBallotStream, root =>
                    root["publishedBallots"]!.AsArray()[0]!.AsObject()["publicationSequence"] = 2, cancellationToken);
                return;

            case "tamper-published-stream-hash":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.PublishedBallotStream, root =>
                    root["publishedBallotStreamHash"] = new string('0', 64), cancellationToken);
                return;

            case "tamper-sp04-receipt-set-hash":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.Sp04Evidence, root =>
                    root["receiptCommitmentSetHash"] = new string('0', 64), cancellationToken);
                return;

            case "tamper-sp04-count":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.Sp04Evidence, root =>
                    root["acceptedBoundReceiptCount"] = root["acceptedBoundReceiptCount"]!.GetValue<int>() + 1, cancellationToken);
                return;

            case "tamper-sp04-accepted-binding":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.AcceptedBallotSet, root =>
                    root["acceptedBallots"]!.AsArray()[0]!.AsObject()["receiptCommitment"] = "tampered-receipt", cancellationToken);
                return;

            case "tamper-sp05-public-named-field":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.Sp05EligibilitySummary, root =>
                    root["displayLabel"] = "Alice Example", cancellationToken);
                return;

            case "tamper-sp05-count-reconciliation":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.Sp05EligibilitySummary, root =>
                    root["didNotVoteCount"] = root["didNotVoteCount"]!.GetValue<int>() + 1, cancellationToken);
                return;

            case "tamper-sp08-release-manifest-hash":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.Sp08ReleaseManifest, root =>
                    root["releaseId"] = "release-tampered", cancellationToken);
                await RefreshPackageRootManifestsAsync(packagePath, cancellationToken);
                return;

            case "tamper-sp08-mutable-artifact-reference":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.Sp08ReleaseManifest, root =>
                    root["components"]!.AsArray()[0]!.AsObject()["immutableReference"] = "latest", cancellationToken);
                await RefreshSp08ReleaseIntegrityHashAsync(packagePath, cancellationToken);
                return;

            case "tamper-sp08-component-hash-mismatch":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.Sp08ReleaseManifest, root =>
                    root["components"]!.AsArray()[0]!.AsObject()["artifactDigest"] = "missing-sha256-prefix", cancellationToken);
                await RefreshSp08ReleaseIntegrityHashAsync(packagePath, cancellationToken);
                return;

            case "tamper-sp08-protocol-package-mismatch":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.Sp08ReleaseIntegrity, root =>
                    root["protocolPackageManifestHash"] = new string('0', 64), cancellationToken);
                await RefreshPackageRootManifestsAsync(packagePath, cancellationToken);
                return;

            case "tamper-sp08-circuit-key-hash-mismatch":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.Sp08ReleaseManifest, root =>
                {
                    var circuit = root["circuitAndKeys"]!.AsArray()[0]!.AsObject();
                    circuit["circuitHash"] = "circuit-hash";
                    circuit["provingKeyHash"] = "proving-key-hash";
                    circuit["verifyingKeyHash"] = "verifying-key-hash";
                }, cancellationToken);
                await RefreshSp08ReleaseIntegrityHashAsync(packagePath, cancellationToken);
                return;

            case "tamper-sp10-forbidden-leak":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.Sp10OperationalSecuritySummary, root =>
                {
                    root["publicPrivacyBoundary"] = new JsonArray("rawLogLine");
                    root["operationalReadinessCaveat"] = "Certified for public elections.";
                }, cancellationToken);
                await RefreshPackageRootManifestsAsync(packagePath, cancellationToken);
                return;

            case "tamper-sp10-kms-public-value-leak":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.Sp10OperationalSecuritySummary, root =>
                    root["primaryIssue"] = "Public summary leaked arn:aws:kms:eu-central-1:111122223333:key/key-secret-123.", cancellationToken);
                await RefreshPackageRootManifestsAsync(packagePath, cancellationToken);
                return;

            case "tamper-stale-verifier-source-ref":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.Sp08ReleaseManifest, root =>
                    root["components"]!.AsArray()[2]!.AsObject()["artifactDigest"] = "missing-sha256-prefix", cancellationToken);
                await RefreshSp08ReleaseIntegrityHashAsync(packagePath, cancellationToken);
                return;

            case "tamper-stale-verifier-binary-hash":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.Sp08ReleaseManifest, root =>
                    root["components"]!.AsArray()[2]!.AsObject()["isPlaceholder"] = true, cancellationToken);
                await RefreshSp08ReleaseIntegrityHashAsync(packagePath, cancellationToken);
                return;

            case "tamper-stale-protocol-package-version":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.Sp08ReleaseIntegrity, root =>
                    root["protocolPackageManifestHash"] = new string('f', 64), cancellationToken);
                await RefreshPackageRootManifestsAsync(packagePath, cancellationToken);
                return;

            case "tamper-package-schema-version-drift":
                await WriteTextLfAsync(
                    ResolvePackagePath(packagePath, VerificationPackageFileNames.VerifierInputManifest),
                    "{",
                    cancellationToken);
                return;

            case "tamper-expected-result-drift":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.ResultBinding, root =>
                    root["expectedResultDriftMarker"] = "expected output hash no longer matches observed verifier output", cancellationToken);
                return;

            case "tamper-corpus-index-drift":
                await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.VerifierProfile, root =>
                    root["profileId"] = VerificationProfileIds.RestrictedOwnerAuditorV1, cancellationToken);
                await RefreshPackageRootManifestsAsync(packagePath, cancellationToken);
                return;

            default:
                throw new InvalidOperationException($"Unknown verifier corpus tamper fixture '{fixtureId}'.");
        }
    }

    private static async Task WriteProfileMarkerAsync(
        string packagePath,
        FixtureSpec spec,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken)
    {
        var marker = new JsonObject
        {
            ["schemaVersion"] = "verifier-corpus-good-sample-profile-marker.v1",
            ["fixtureId"] = spec.FixtureId,
            ["corpusProfileId"] = spec.CorpusProfileId,
            ["description"] = spec.ProfileDescription,
            ["generatedAt"] = generatedAt.UtcDateTime.ToString("O"),
            ["publicBoundary"] = "Synthetic profile marker only; this file is not restricted election evidence.",
        };

        await WriteTextLfAsync(
            Path.Combine(packagePath, "profile-marker.json"),
            CanonicalJson(marker),
            cancellationToken);
    }

    private static async Task RefreshSp08ReleaseIntegrityHashAsync(string packagePath, CancellationToken cancellationToken)
    {
        var releaseManifest = await ReadJsonArtifactAsync<ElectionSp08ReleaseManifestArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp08ReleaseManifest,
            cancellationToken);
        await MutateJsonArtifactAsync(packagePath, VerificationPackageFileNames.Sp08ReleaseIntegrity, root =>
            root["releaseManifestHash"] = ElectionSp08ReleaseManifestHasher.ComputeReleaseManifestHash(releaseManifest), cancellationToken);
        await RefreshPackageRootManifestsAsync(packagePath, cancellationToken);
    }

    private static async Task MutateJsonArtifactAsync(
        string packagePath,
        string relativePath,
        Action<JsonObject> mutate,
        CancellationToken cancellationToken)
    {
        var path = ResolvePackagePath(packagePath, relativePath);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken))?.AsObject() ??
            throw new InvalidOperationException($"Package artifact '{relativePath}' is not a JSON object.");
        mutate(root);
        await WriteTextLfAsync(path, root.ToJsonString(JsonOptions), cancellationToken);
    }

    private static async Task<T> ReadJsonArtifactAsync<T>(
        string packagePath,
        string relativePath,
        CancellationToken cancellationToken) =>
        JsonSerializer.Deserialize<T>(
            await File.ReadAllTextAsync(ResolvePackagePath(packagePath, relativePath), cancellationToken),
            JsonOptions)!;

    private static async Task WriteJsonArtifactAsync<T>(
        string packagePath,
        string relativePath,
        T value,
        CancellationToken cancellationToken) =>
        await WriteTextLfAsync(
            ResolvePackagePath(packagePath, relativePath),
            JsonSerializer.Serialize(value, JsonOptions),
            cancellationToken);

    private static async Task RefreshPackageRootManifestsAsync(string packagePath, CancellationToken cancellationToken)
    {
        var manifest = await ReadJsonArtifactAsync<AuditPackageManifestRecord>(
            packagePath,
            VerificationPackageFileNames.AuditPackageManifest,
            cancellationToken);
        var refreshedEntries = new List<AuditPackageManifestEntryRecord>();
        foreach (var entry in manifest.Entries)
        {
            var path = ResolvePackagePath(packagePath, entry.Path);
            if (!File.Exists(path))
            {
                refreshedEntries.Add(entry);
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            refreshedEntries.Add(entry with
            {
                Sha256Hash = VerificationCanonicalHash.ComputeManifestFileSha256(bytes),
                SizeBytes = bytes.Length,
            });
        }

        var refreshedManifest = manifest with { Entries = refreshedEntries };
        await WriteJsonArtifactAsync(packagePath, VerificationPackageFileNames.AuditPackageManifest, refreshedManifest, cancellationToken);

        var inputManifest = await ReadJsonArtifactAsync<VerifierInputManifestRecord>(
            packagePath,
            VerificationPackageFileNames.VerifierInputManifest,
            cancellationToken);
        var manifestBytes = await File.ReadAllBytesAsync(
            ResolvePackagePath(packagePath, VerificationPackageFileNames.AuditPackageManifest),
            cancellationToken);
        await WriteJsonArtifactAsync(
            packagePath,
            VerificationPackageFileNames.VerifierInputManifest,
            inputManifest with
            {
                AuditPackageManifestHash = VerificationCanonicalHash.ComputeManifestFileSha256(manifestBytes),
            },
            cancellationToken);
    }

    private static string ComputeDirectoryHash(string root)
    {
        var builder = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(path => Path.GetRelativePath(root, path).Replace('\\', '/'), StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
            var fileHash = Sha256Bytes(File.ReadAllBytes(file));
            builder.Append(relativePath).Append('|').Append(fileHash).Append('\n');
        }

        return Sha256Text(builder.ToString());
    }

    public static string CanonicalJson(JsonNode node) =>
        node.ToJsonString(JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";

    private static async Task WriteTextLfAsync(string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, NormalizeLineEndings(content), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
    }

    private static void NormalizeTextFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(file);
            if (!string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var content = File.ReadAllText(file, Encoding.UTF8);
            var normalized = NormalizeLineEndings(content);
            if (!string.Equals(content, normalized, StringComparison.Ordinal))
            {
                File.WriteAllText(file, normalized, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
    }

    private static string NormalizeLineEndings(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

    private static string Sha256Text(string content) =>
        Sha256Bytes(Encoding.UTF8.GetBytes(content));

    private static string Sha256Bytes(byte[] bytes) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";

    private static string ToJsonEnumString<T>(T value)
        where T : struct, Enum =>
        JsonSerializer.Serialize(value, JsonOptions).Trim('"');

    private static string ResolvePackagePath(string packagePath, string relativePath) =>
        Path.Combine(packagePath, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static void EnsurePathUnder(string root, string path, string label)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Generated corpus path escapes output root: {label}");
        }
    }

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

    private sealed record FixtureSpec(
        string FixtureId,
        string CorpusProfileId,
        string ProfileDescription,
        string ProfileId,
        string ExpectedResultCode,
        VerificationCheckStatus ExpectedCheckStatus,
        VerificationOverallStatus ExpectedOverallStatus,
        int ExpectedExitCode,
        bool SecondaryFailuresAllowed = true)
    {
        public FixtureSpec(
            string fixtureId,
            string profileId,
            string expectedResultCode,
            VerificationCheckStatus expectedCheckStatus,
            VerificationOverallStatus expectedOverallStatus,
            int expectedExitCode,
            bool SecondaryFailuresAllowed = true)
            : this(
                fixtureId,
                ResolveDefaultCorpusProfileId(fixtureId),
                ResolveDefaultProfileDescription(fixtureId),
                profileId,
                expectedResultCode,
                expectedCheckStatus,
                expectedOverallStatus,
                expectedExitCode,
                SecondaryFailuresAllowed)
        {
        }
    }

    private static string ResolveDefaultCorpusProfileId(string fixtureId) =>
        fixtureId.StartsWith("tamper-stale-", StringComparison.Ordinal) ||
        fixtureId.Contains("-drift", StringComparison.Ordinal)
            ? "stale_version_drift"
            : "baseline_finalized";

    private static string ResolveDefaultProfileDescription(string fixtureId) =>
        fixtureId.StartsWith("tamper-", StringComparison.Ordinal)
            ? "Synthetic tamper fixture derived from the FEAT-135 baseline good sample."
            : "Baseline finalized organizational election from FEAT-135.";
}
