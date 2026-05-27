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
