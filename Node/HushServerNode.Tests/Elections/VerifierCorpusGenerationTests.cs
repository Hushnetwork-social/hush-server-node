using System.Text.Json.Nodes;
using FluentAssertions;
using HushShared.Elections.Verification.Model;
using VerifierCorpusPromoter;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class VerifierCorpusGenerationTests
{
    private static readonly DateTimeOffset FixedGeneratedAt = DateTimeOffset.Parse("2026-05-20T00:00:00Z");

    [Fact]
    public async Task Generate_GoodSample_ShouldPassPublicAnonymousVerifier()
    {
        using var workspace = TempCorpusWorkspace.Create();

        var result = await GenerateAsync(workspace.Root);

        result.GoodSample.ExpectedOverallStatus.Should().Be(VerificationOverallStatus.Pass);
        result.GoodSample.ExpectedExitCode.Should().Be(0);
        result.GoodSample.ExpectedPrimaryResultCode.Should().Be(VerificationResultCodes.PackageStructureValid);
        Directory.EnumerateFiles(
                Path.Combine(workspace.Root, "packages", VerifierCorpusGenerator.GoodSampleFixtureId),
                "*",
                SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Path.Combine(workspace.Root, "packages", VerifierCorpusGenerator.GoodSampleFixtureId), path).Replace('\\', '/'))
            .Should()
            .NotContain(path => path.StartsWith("artifacts/restricted/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Generate_RequiredTamperFixtures_ShouldReportExpectedPrimaryFailures()
    {
        using var workspace = TempCorpusWorkspace.Create();

        var result = await GenerateAsync(workspace.Root);

        result.Fixtures.Select(x => x.FixtureId)
            .Should()
            .BeEquivalentTo(VerifierCorpusContracts.RequiredFixtureIds);
        result.Fixtures.Where(x => x.FixtureId != VerifierCorpusGenerator.GoodSampleFixtureId)
            .Should()
            .OnlyContain(x =>
                x.ExpectedOverallStatus == VerificationOverallStatus.Fail ||
                x.ExpectedOverallStatus == VerificationOverallStatus.NotAvailable);

        foreach (var fixture in result.Fixtures)
        {
            var expectedResultPath = Path.Combine(workspace.Root, "expected-results", $"{fixture.FixtureId}.json");
            File.Exists(expectedResultPath).Should().BeTrue(fixture.FixtureId);
            var expected = JsonNode.Parse(await File.ReadAllTextAsync(expectedResultPath))!.AsObject();
            expected["requiredResultCodes"]!.AsArray()
                .Select(x => x!.GetValue<string>())
                .Should()
                .Contain(fixture.ExpectedPrimaryResultCode, fixture.FixtureId);
        }
    }

    [Fact]
    public async Task Generate_PublicArtifacts_ShouldValidateContractsAndScanClean()
    {
        using var workspace = TempCorpusWorkspace.Create();

        var result = await GenerateAsync(workspace.Root);

        result.NoSecretScanStatus.Should().Be("pass");
        result.ScanFindings.Should().OnlyContain(x => x.ExpectedTamperFixture);
        result.ScanFindings.Should().NotBeEmpty("SP-10 tamper fixtures intentionally prove synthetic forbidden markers fail closed");

        VerifierCorpusContracts.ValidateCorpusManifest(ReadJson("corpus-manifest.json", workspace.Root))
            .Should()
            .BeEmpty();
        VerifierCorpusContracts.ValidateReadinessFragment(ReadJson("readiness/verifier-corpus-readiness-fragment.json", workspace.Root))
            .Should()
            .BeEmpty();
        VerifierCorpusContracts.ValidateDownstreamHandoff(ReadJson("handoff/verifier-corpus-downstream-handoff.json", workspace.Root))
            .Should()
            .BeEmpty();

        foreach (var fixtureId in VerifierCorpusContracts.RequiredFixtureIds)
        {
            VerifierCorpusContracts.ValidateFixtureManifest(
                    ReadJson($"fixtures/{fixtureId}/fixture-manifest.json", workspace.Root),
                    fixtureId)
                .Should()
                .BeEmpty(fixtureId);
        }

        var readme = await File.ReadAllTextAsync(Path.Combine(workspace.Root, "README.md"));
        readme.Should().Contain("PowerShell good-sample run");
        readme.Should().Contain("Bash good-sample run");
        readme.Should().NotContain("FEAT-135");
        readme.Should().NotContain("EPIC-015");
    }

    [Fact]
    public async Task Generate_RefreshRelease_ShouldAddBreadthDriftAndScoreArtifacts()
    {
        using var workspace = TempCorpusWorkspace.Create();

        var result = await GenerateRefreshAsync(workspace.Root);

        result.Fixtures.Select(x => x.FixtureId)
            .Should()
            .BeEquivalentTo(VerifierCorpusGenerator.RefreshFixtureIds());
        var goodSamples = result.Fixtures
            .Where(x => x.FixtureId.StartsWith("sample-good-", StringComparison.Ordinal))
            .ToArray();
        goodSamples
            .Should()
            .HaveCount(5)
            .And.OnlyContain(x =>
                x.ExpectedOverallStatus == VerificationOverallStatus.Pass &&
                x.ExpectedExitCode == VerificationExitCodes.Pass);
        goodSamples.Select(x => x.PackageHash)
            .Should()
            .OnlyHaveUniqueItems("refresh good samples must be separately exported package shapes, not marker-only clones");

        ReadPackageArtifact("sample-good-finalized-election", VerificationPackageFileNames.AcceptedBallotSet)
            ["acceptedBallotCount"]!.GetValue<int>().Should().Be(2);
        ReadPackageArtifact("sample-good-larger-electorate", VerificationPackageFileNames.AcceptedBallotSet)
            ["acceptedBallotCount"]!.GetValue<int>().Should().Be(4);
        ReadPackageArtifact("sample-good-low-turnout", VerificationPackageFileNames.Sp05EligibilitySummary)
            ["didNotVoteCount"]!.GetValue<int>().Should().Be(5);
        ReadPackageArtifact("sample-good-trustee-threshold", VerificationPackageFileNames.Sp06TrusteeControlSummary)
            ["acceptedReleaseArtifactCount"]!.GetValue<int>().Should().Be(3);

        result.Fixtures.Where(x => x.CorpusProfileId == "stale_version_drift")
            .Should()
            .HaveCount(6)
            .And.OnlyContain(x => x.ExpectedOverallStatus != VerificationOverallStatus.Pass);

        File.Exists(Path.Combine(workspace.Root, "validation", "result-code-stability-summary.json")).Should().BeTrue();
        File.Exists(Path.Combine(workspace.Root, "validation", "stale-version-drift-check.json")).Should().BeTrue();
        File.Exists(Path.Combine(workspace.Root, "readiness", "verifier-corpus-refresh-score-proposal.json")).Should().BeTrue();
        File.Exists(Path.Combine(workspace.Root, "release-delta-report.md")).Should().BeTrue();

        var scoreProposal = ReadJson("readiness/verifier-corpus-refresh-score-proposal.json", workspace.Root);
        scoreProposal["dimensionId"]!.GetValue<string>().Should().Be("RDY-DIM-002");
        scoreProposal["proposedScoreFrom"]!.GetValue<int>().Should().Be(6);
        scoreProposal["proposedScoreTo"]!.GetValue<int>().Should().Be(8);
        scoreProposal["doesNotMutateRegister"]!.GetValue<bool>().Should().BeTrue();

        var handoff = ReadJson("handoff/verifier-corpus-refresh-downstream-handoff.json", workspace.Root);
        handoff["producerFeature"]!.GetValue<string>().Should().Be("FEAT-151");
        handoff["feat154ConsumerInstructions"].Should().NotBeNull();
        handoff["feat155ConsumerInstructions"].Should().NotBeNull();
        handoff["feat156ConsumerInstructions"].Should().NotBeNull();

        JsonObject ReadPackageArtifact(string fixtureId, string artifactPath) =>
            ReadJson($"packages/{fixtureId}/{artifactPath}", workspace.Root);
    }

    [Fact]
    public async Task Generate_Audit95Release_ShouldAddMatrixGoodSamplesAndTamperFixtures()
    {
        using var workspace = TempCorpusWorkspace.Create();

        var result = await GenerateAudit95Async(workspace.Root);

        result.Fixtures.Select(x => x.FixtureId)
            .Should()
            .BeEquivalentTo(VerifierCorpusGenerator.Audit95FixtureIds());
        result.NoSecretScanStatus.Should().Be("pass");
        result.ScanFindings.Should().OnlyContain(x => x.ExpectedTamperFixture);

        var goodSamples = result.Fixtures
            .Where(x => x.FixtureId.StartsWith("sample-good-", StringComparison.Ordinal))
            .ToArray();
        goodSamples
            .Should()
            .HaveCount(8)
            .And.OnlyContain(x =>
                x.ExpectedOverallStatus == VerificationOverallStatus.Pass &&
                x.ExpectedExitCode == VerificationExitCodes.Pass &&
                x.ExpectedPrimaryResultCode == VerificationResultCodes.PackageStructureValid);
        goodSamples.Select(x => x.PackageHash)
            .Should()
            .OnlyHaveUniqueItems("audit-95 good samples must cover separate election/package shapes");

        result.Fixtures.Select(x => x.FixtureId)
            .Should()
            .Contain([
                "sample-good-binding-style-metadata",
                "sample-good-internal-rehearsal-metadata",
                "sample-good-production-rollout-simulation",
                "tamper-stale-corpus-public-ref",
                "tamper-wrong-package-version",
                "tamper-altered-tally-replay",
                "tamper-sp04-altered-receipt-commitment",
                "tamper-verifier-output-mismatch",
                "tamper-fixture-index-drift",
                "tamper-expected-result-drift",
                "tamper-unsupported-live-dependency",
                "tamper-sp10-public-safe-forbidden-material",
            ]);

        result.Fixtures.Where(x => !x.FixtureId.StartsWith("sample-good-", StringComparison.Ordinal))
            .Should()
            .OnlyContain(x => x.ExpectedOverallStatus != VerificationOverallStatus.Pass);
        result.Fixtures.Single(x => x.FixtureId == "tamper-altered-tally-replay")
            .ExpectedPrimaryResultCode.Should().Be(VerificationResultCodes.PublicationProofTallyReplayMismatch);
        result.Fixtures.Single(x => x.FixtureId == "tamper-sp04-altered-receipt-commitment")
            .ExpectedPrimaryResultCode.Should().Be(VerificationResultCodes.ChallengeSpoilReceiptMismatch);
        result.Fixtures.Single(x => x.FixtureId == "tamper-sp10-public-safe-forbidden-material")
            .ExpectedPrimaryResultCode.Should().Be(VerificationResultCodes.OperationalSecurityForbiddenMaterial);

        foreach (var fixture in result.Fixtures)
        {
            var expectedResultPath = Path.Combine(workspace.Root, "expected-results", $"{fixture.FixtureId}.json");
            File.Exists(expectedResultPath).Should().BeTrue(fixture.FixtureId);
            var expected = JsonNode.Parse(await File.ReadAllTextAsync(expectedResultPath))!.AsObject();
            expected["expectedPrimaryResultCode"]!.GetValue<string>().Should().Be(fixture.ExpectedPrimaryResultCode);
            expected["requiredResultCodes"]!.AsArray()
                .Select(x => x!.GetValue<string>())
                .Should()
                .Contain(fixture.ExpectedPrimaryResultCode, fixture.FixtureId);
            expected["normalizedOutputHash"]!.GetValue<string>().Should().StartWith("sha256:");
            File.Exists(Path.Combine(workspace.Root, "validation", "verifier-output", fixture.FixtureId, "VerifierOutput.json"))
                .Should()
                .BeTrue(fixture.FixtureId);
        }

        ReadPackageArtifact("sample-good-internal-rehearsal-metadata", "profile-marker.json")
            ["corpusProfileId"]!.GetValue<string>().Should().Be("internal_rehearsal_metadata");
        ReadPackageArtifact("sample-good-binding-style-metadata", "profile-marker.json")
            ["corpusProfileId"]!.GetValue<string>().Should().Be("binding_style_metadata");
        var handoff = ReadJson("handoff/verifier-corpus-downstream-handoff.json", workspace.Root);
        handoff["producerFeature"]!.GetValue<string>().Should().Be("FEAT-158");
        handoff["feat159ConsumerInstructions"].Should().NotBeNull();
        handoff["feat160ConsumerInstructions"].Should().NotBeNull();
        handoff["feat163ConsumerInstructions"].Should().NotBeNull();
        handoff["feat166ConsumerInstructions"].Should().NotBeNull();
        (await File.ReadAllTextAsync(Path.Combine(workspace.Root, "release-delta-report.md")))
            .Should()
            .Contain("RDY-DIM-002 8 -> 10");

        JsonObject ReadPackageArtifact(string fixtureId, string artifactPath) =>
            ReadJson($"packages/{fixtureId}/{artifactPath}", workspace.Root);
    }

    [Fact]
    public async Task CiReplay_Audit95Release_ShouldProduceManifestSummaryAndMatchAllFixtures()
    {
        using var workspace = TempCorpusWorkspace.Create();
        await GenerateAudit95Async(workspace.Root);

        var result = await ReplayAudit95Async(workspace.Root);

        result.Passed.Should().BeTrue(string.Join("; ", result.ContractErrors));
        result.RunStatus.Should().Be("accepted");
        result.PublicSafetyStatus.Should().Be("pass");
        result.FixtureCount.Should().Be(VerifierCorpusGenerator.Audit95FixtureIds().Count);
        result.MatchedFixtureCount.Should().Be(result.FixtureCount);
        result.MismatchCount.Should().Be(0);

        var manifest = ReadJson(VerifierCorpusCiReplayRunner.ManifestRelativePath, workspace.Root);
        VerifierCorpusContracts.ValidateCiRunManifest(manifest).Should().BeEmpty();
        manifest["fixtureCount"]!.GetValue<int>().Should().Be(result.FixtureCount);
        manifest["mismatchCount"]!.GetValue<int>().Should().Be(0);
        manifest["fixtures"]!.AsArray()
            .Select(x => x!.AsObject()["status"]!.GetValue<string>())
            .Should()
            .OnlyContain(x => x == "matched");
        File.Exists(Path.Combine(workspace.Root, VerifierCorpusCiReplayRunner.SummaryJsonRelativePath.Replace('/', Path.DirectorySeparatorChar)))
            .Should()
            .BeTrue();
        File.Exists(Path.Combine(workspace.Root, VerifierCorpusCiReplayRunner.SummaryMarkdownRelativePath.Replace('/', Path.DirectorySeparatorChar)))
            .Should()
            .BeTrue();
        var reviewerHandoff = ReadJson(VerifierCorpusCiReplayRunner.PublicReviewerHandoffRelativePath, workspace.Root);
        reviewerHandoff["producerFeature"]!.GetValue<string>().Should().Be("FEAT-158");
        reviewerHandoff["replayEvidence"]!["fixtureCount"]!.GetValue<int>().Should().Be(result.FixtureCount);
        reviewerHandoff["cleanMachineReplay"]!["privateRepositoriesRequired"]!.GetValue<bool>().Should().BeFalse();
        reviewerHandoff["downstreamOwners"]!.AsArray()
            .Select(x => x!.AsObject()["featureId"]!.GetValue<string>())
            .Should()
            .Contain(["FEAT-159", "FEAT-160", "FEAT-163", "FEAT-166"]);
        var readinessFragment = ReadJson(VerifierCorpusCiReplayRunner.Audit95ReadinessFragmentRelativePath, workspace.Root);
        readinessFragment["targetBlocker"]!.GetValue<string>().Should().Be("RDY-BLOCK-INTERNAL_AUDIT_95_DIM002-001");
        readinessFragment["doesNotMutateRegister"]!.GetValue<bool>().Should().BeTrue();
        var scoreProposal = ReadJson(VerifierCorpusCiReplayRunner.Audit95ScoreProposalRelativePath, workspace.Root);
        VerifierCorpusContracts.ValidateAudit95ScoreProposal(scoreProposal).Should().BeEmpty();
        scoreProposal["proposedScoreFrom"]!.GetValue<int>().Should().Be(8);
        scoreProposal["proposedScoreTo"]!.GetValue<int>().Should().Be(10);
        scoreProposal["targetBlocker"]!["proposedStatus"]!.GetValue<string>().Should().Be("green/resolved");
    }

    [Fact]
    public async Task CiReplay_StaleExpectedOutputHash_ShouldFailWithMismatch()
    {
        using var workspace = TempCorpusWorkspace.Create();
        await GenerateAudit95Async(workspace.Root);
        var expectedResultPath = Path.Combine(
            workspace.Root,
            "expected-results",
            $"{VerifierCorpusGenerator.GoodSampleFixtureId}.json");
        var expectedResult = JsonNode.Parse(await File.ReadAllTextAsync(expectedResultPath))!.AsObject();
        expectedResult["normalizedOutputHash"] = "sha256:ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
        await File.WriteAllTextAsync(expectedResultPath, VerifierCorpusGenerator.CanonicalJson(expectedResult));

        var result = await ReplayAudit95Async(workspace.Root);

        result.Passed.Should().BeFalse();
        result.RunStatus.Should().Be("failed");
        result.MismatchCount.Should().Be(1);
        result.Fixtures.Single(x => x.FixtureId == VerifierCorpusGenerator.GoodSampleFixtureId)
            .MismatchReasons.Should().Contain(x => x.Contains("normalized-output-hash", StringComparison.Ordinal));
        ReadJson(VerifierCorpusCiReplayRunner.Audit95ScoreProposalRelativePath, workspace.Root)
            ["status"]!.GetValue<string>()
            .Should()
            .Be("blocked");
    }

    [Fact]
    public async Task CiReplay_UnexpectedForbiddenPublicMaterial_ShouldBlockRun()
    {
        using var workspace = TempCorpusWorkspace.Create();
        await GenerateAudit95Async(workspace.Root);
        await File.AppendAllTextAsync(
            Path.Combine(workspace.Root, "README.md"),
            "\nSynthetic regression marker: token=secret-value\n");

        var result = await ReplayAudit95Async(workspace.Root);

        result.Passed.Should().BeFalse();
        result.RunStatus.Should().Be("blocked");
        result.PublicSafetyStatus.Should().Be("blocked");
        result.UnexpectedPublicFindingCount.Should().BeGreaterThan(0);
        ReadJson(VerifierCorpusCiReplayRunner.ManifestRelativePath, workspace.Root)
            ["unexpectedPublicFindingCount"]!.GetValue<int>()
            .Should()
            .Be(result.UnexpectedPublicFindingCount);
    }

    [Fact]
    public async Task Generate_PlatformReplayFlags_ShouldFlowToValidationSummaryAndHandoff()
    {
        using var workspace = TempCorpusWorkspace.Create();

        await new VerifierCorpusGenerator().GenerateAsync(new VerifierCorpusGenerationOptions(
            workspace.Root,
            "v0.1.0",
            FixedGeneratedAt,
            WindowsReviewerReplayValidated: true,
            LinuxReviewerReplayValidated: true));

        var summary = ReadJson("validation/clean-machine-validation-summary.json", workspace.Root);
        summary["windows"]!["status"]!.GetValue<string>().Should().Be("pass");
        summary["windows"]!["validated"]!.GetValue<bool>().Should().BeTrue();
        summary["linux"]!["status"]!.GetValue<string>().Should().Be("pass");
        summary["linux"]!["validated"]!.GetValue<bool>().Should().BeTrue();

        var handoff = ReadJson("handoff/verifier-corpus-downstream-handoff.json", workspace.Root);
        handoff["cleanMachineValidationSummary"]!["status"]!.GetValue<string>()
            .Should()
            .Be("windows_linux_replay_validated");
    }

    [Fact]
    public async Task Generate_RepeatedRuns_ShouldProduceStablePackageAndOutputHashes()
    {
        using var first = TempCorpusWorkspace.Create();
        using var second = TempCorpusWorkspace.Create();

        var firstResult = await GenerateAsync(first.Root);
        var secondResult = await GenerateAsync(second.Root);

        firstResult.Fixtures.Select(x => (x.FixtureId, x.PackageHash, x.NormalizedOutputHash))
            .Should()
            .Equal(secondResult.Fixtures.Select(x => (x.FixtureId, x.PackageHash, x.NormalizedOutputHash)));
        firstResult.ManifestHash.Should().Be(secondResult.ManifestHash);
        firstResult.FixtureIndexHash.Should().Be(secondResult.FixtureIndexHash);
        firstResult.NoSecretScanStatus.Should().Be(secondResult.NoSecretScanStatus);
    }

    [Fact]
    public void ReadinessEvaluator_GoodSampleWarning_BlocksAcceptance()
    {
        var warningGoodSample = new VerifierCorpusFixtureGenerationResult(
            VerifierCorpusGenerator.GoodSampleFixtureId,
            "packages/sample-good-finalized-election",
            VerificationProfileIds.PublicAnonymousV1,
            "baseline_finalized",
            "Baseline finalized organizational election from FEAT-135.",
            VerificationResultCodes.ExternalReviewNotComplete,
            VerificationCheckStatus.Warn,
            VerificationOverallStatus.Warn,
            VerificationExitCodes.Pass,
            "sha256:0000000000000000000000000000000000000000000000000000000000000000",
            "sha256:1111111111111111111111111111111111111111111111111111111111111111",
            SecondaryFailuresAllowed: false);

        VerifierCorpusReadinessEvaluator.GoodSampleBlocksAcceptance(warningGoodSample).Should().BeTrue();
    }

    [Fact]
    public void PublicScanner_UnexpectedSecretText_ShouldFailClosed()
    {
        var findings = VerifierCorpusGenerator.ScanTextForForbiddenPublicMaterial(
            "README.md",
            "Do not publish token=secret-value in public corpus output.");

        findings.Should().ContainSingle(x =>
            x.Category == "cloud_secret" &&
            x.ExpectedTamperFixture == false);
    }

    [Fact]
    public async Task PublicScanner_ExpectedCiReplayTamperOutput_ShouldRemainExpected()
    {
        using var workspace = TempCorpusWorkspace.Create();
        var outputPath = Path.Combine(
            workspace.Root,
            "validation",
            "ci-verifier-output",
            "tamper-sp10-public-safe-forbidden-material",
            "VerifierOutput.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, "{\"message\":\"aws_secret_access_key\"}");

        var findings = VerifierCorpusGenerator.ScanPublicOutput(workspace.Root);

        findings.Should().ContainSingle(x =>
            x.Category == "cloud_secret" &&
            x.ExpectedTamperFixture);
    }

    [Fact]
    public async Task Promotion_ValidateOnly_ShouldNotWritePublicRoot()
    {
        using var workspace = TempVerifierCorpusPromotionWorkspace.Create();
        var paths = workspace.Paths;
        var publicRootExistsBefore = Directory.Exists(paths.PublicOutputRoot);
        var before = publicRootExistsBefore
            ? Directory.EnumerateFiles(paths.PublicOutputRoot, "*", SearchOption.AllDirectories)
                .Where(path => !Path.GetRelativePath(paths.PublicOutputRoot, path).Replace('\\', '/').StartsWith(".git/", StringComparison.Ordinal))
                .Select(path => (Path.GetRelativePath(paths.PublicOutputRoot, path), File.GetLastWriteTimeUtc(path)))
                .OrderBy(x => x.Item1, StringComparer.Ordinal)
                .ToArray()
            : [];

        var result = await new VerifierCorpusPromotionService().PromoteAsync(new VerifierCorpusPromotionOptions(
            paths,
            paths.PublicOutputRoot,
            "v0.1.0",
            FixedGeneratedAt,
            ValidateOnly: true,
            CheckOnly: false,
            PublicRepositoryRef: "test-ref",
            VerifierSourceRef: "test-source-ref",
            VerifierHash: "sha256:test-verifier-hash"));

        result.Mode.Should().Be(VerifierCorpusPromotionService.ModeValidateOnly);
        result.WrittenFiles.Should().BeEmpty();
        Directory.Exists(paths.PublicOutputRoot).Should().Be(publicRootExistsBefore);
        if (publicRootExistsBefore)
        {
            var after = Directory.EnumerateFiles(paths.PublicOutputRoot, "*", SearchOption.AllDirectories)
                .Where(path => !Path.GetRelativePath(paths.PublicOutputRoot, path).Replace('\\', '/').StartsWith(".git/", StringComparison.Ordinal))
                .Select(path => (Path.GetRelativePath(paths.PublicOutputRoot, path), File.GetLastWriteTimeUtc(path)))
                .OrderBy(x => x.Item1, StringComparer.Ordinal)
                .ToArray();
            after.Should().Equal(before);
        }
    }

    [Fact]
    public async Task Promotion_Generate_ShouldWriteVersionedRepositoryLayout()
    {
        using var workspace = TempVerifierCorpusPromotionWorkspace.Create();
        var paths = workspace.Paths;
        var publicRoot = paths.PublicOutputRoot;

        var result = await new VerifierCorpusPromotionService().PromoteAsync(new VerifierCorpusPromotionOptions(
            paths,
            publicRoot,
            "v0.1.0",
            FixedGeneratedAt,
            ValidateOnly: false,
            CheckOnly: false,
            PublicRepositoryRef: "test-ref",
            VerifierSourceRef: "test-source-ref",
            VerifierHash: "sha256:test-verifier-hash",
            WindowsReviewerReplayValidated: true,
            LinuxReviewerReplayValidated: true));

        result.OutputRoot.Replace('\\', '/').Should().EndWith("/HushVoting-Verifier-Corpus/hushvoting-v1/v0.1.0");
        File.Exists(Path.Combine(publicRoot, "README.md")).Should().BeTrue();
        File.Exists(Path.Combine(publicRoot, VerifierCorpusPromotionService.CorpusIndexFileName)).Should().BeTrue();
        File.Exists(Path.Combine(publicRoot, "hushvoting-v1", "v0.1.0", "corpus-manifest.json")).Should().BeTrue();
        Directory.Exists(Path.Combine(publicRoot, "packages")).Should().BeFalse("the repository root must not mix versioned corpus package files");

        var index = ReadJson(VerifierCorpusPromotionService.CorpusIndexFileName, publicRoot);
        index["latest"]!["path"]!.GetValue<string>().Should().Be("hushvoting-v1/v0.1.0/corpus-manifest.json");

        var versionReadme = await File.ReadAllTextAsync(Path.Combine(publicRoot, "hushvoting-v1", "v0.1.0", "README.md"));
        versionReadme.Should().Contain("..\\..\\..\\hush-server-node");
        versionReadme.Should().Contain("../../../hush-server-node");
    }

    [Fact]
    public async Task Promotion_OutputRootOutsideWorkspace_ShouldFailBeforeWriting()
    {
        using var workspace = TempVerifierCorpusPromotionWorkspace.Create();
        var paths = workspace.Paths;
        var outsideRoot = Path.Combine(Path.GetTempPath(), "HushVoting-Verifier-Corpus");

        var act = async () => await new VerifierCorpusPromotionService().PromoteAsync(new VerifierCorpusPromotionOptions(
            paths,
            outsideRoot,
            "v0.1.0",
            FixedGeneratedAt,
            ValidateOnly: false,
            CheckOnly: false,
            PublicRepositoryRef: "test-ref",
            VerifierSourceRef: "test-source-ref",
            VerifierHash: "sha256:test-verifier-hash"));

        await act.Should().ThrowAsync<VerifierCorpusPromotionException>()
            .WithMessage("*escapes workspace root*");
    }

    private static Task<VerifierCorpusGenerationResult> GenerateAsync(string root) =>
        new VerifierCorpusGenerator().GenerateAsync(new VerifierCorpusGenerationOptions(
            root,
            "v0.1.0",
            FixedGeneratedAt));

    private static Task<VerifierCorpusGenerationResult> GenerateRefreshAsync(string root) =>
        new VerifierCorpusGenerator().GenerateAsync(new VerifierCorpusGenerationOptions(
            root,
            "v0.2.0",
            FixedGeneratedAt));

    private static Task<VerifierCorpusGenerationResult> GenerateAudit95Async(string root) =>
        new VerifierCorpusGenerator().GenerateAsync(new VerifierCorpusGenerationOptions(
            root,
            "v0.3.0",
            FixedGeneratedAt));

    private static Task<VerifierCorpusCiReplayResult> ReplayAudit95Async(string root) =>
        new VerifierCorpusCiReplayRunner().ReplayAsync(new VerifierCorpusCiReplayOptions(
            root,
            FixedGeneratedAt,
            CorpusRepositoryRef: "1111111111111111111111111111111111111111",
            VerifierSourceRef: "2222222222222222222222222222222222222222",
            VerifierHash: "sha256:3333333333333333333333333333333333333333333333333333333333333333",
            WorkflowName: "test-verifier-corpus-ci",
            WorkflowRunId: "test-run"));

    private static JsonObject ReadJson(string relativePath, string root) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar))))!.AsObject();

    private sealed class TempCorpusWorkspace : IDisposable
    {
        private TempCorpusWorkspace(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TempCorpusWorkspace Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"hush-verifier-corpus-{Guid.NewGuid():N}");
            return new TempCorpusWorkspace(root);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class TempVerifierCorpusPromotionWorkspace : IDisposable
    {
        private TempVerifierCorpusPromotionWorkspace(string root)
        {
            Root = root;
            Paths = VerifierCorpusPromotionPaths.FromWorkspaceRoot(root);
        }

        public string Root { get; }

        public VerifierCorpusPromotionPaths Paths { get; }

        public static TempVerifierCorpusPromotionWorkspace Create() =>
            new(HushVotingReadinessTestArtifacts.CreateVerifierCorpusWorkspace());

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
