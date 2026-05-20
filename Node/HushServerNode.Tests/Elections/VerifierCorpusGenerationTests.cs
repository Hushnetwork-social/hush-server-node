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
        var workspaceRoot = WorkspaceRootFinder.Find(Directory.GetCurrentDirectory());
        var paths = VerifierCorpusPromotionPaths.FromWorkspaceRoot(workspaceRoot);
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
        var workspaceRoot = WorkspaceRootFinder.Find(Directory.GetCurrentDirectory());
        var paths = VerifierCorpusPromotionPaths.FromWorkspaceRoot(workspaceRoot);
        var publicRoot = Path.Combine(
            workspaceRoot,
            ".tmp-feat135-tests",
            Guid.NewGuid().ToString("N"),
            "HushVoting-Verifier-Corpus");

        try
        {
            var result = await new VerifierCorpusPromotionService().PromoteAsync(new VerifierCorpusPromotionOptions(
                paths,
                publicRoot,
                "v0.1.0",
                FixedGeneratedAt,
                ValidateOnly: false,
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
        finally
        {
            var tempRoot = Path.Combine(workspaceRoot, ".tmp-feat135-tests");
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Promotion_OutputRootOutsideWorkspace_ShouldFailBeforeWriting()
    {
        var workspaceRoot = WorkspaceRootFinder.Find(Directory.GetCurrentDirectory());
        var paths = VerifierCorpusPromotionPaths.FromWorkspaceRoot(workspaceRoot);
        var outsideRoot = Path.Combine(Path.GetTempPath(), "HushVoting-Verifier-Corpus");

        var act = async () => await new VerifierCorpusPromotionService().PromoteAsync(new VerifierCorpusPromotionOptions(
            paths,
            outsideRoot,
            "v0.1.0",
            FixedGeneratedAt,
            ValidateOnly: false,
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
}
