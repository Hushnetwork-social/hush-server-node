using System.Text.Json.Nodes;
using FailedFinalizeContinuityRehearsalPromoter;
using FluentAssertions;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class FailedFinalizeContinuityPromotionServiceTests
{
    [Fact]
    public void PromotionService_ValidateOnly_DoesNotWritePackageOutputs()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var paths = CreatePromotionPaths(tempRoot);

            var result = new FailedFinalizeContinuityPromotionService().Promote(new(
                paths,
                FailedFinalizeContinuityPromotionService.ModeValidateOnly,
                SourceInput: null,
                OutputRoot: null,
                GeneratedAt: FixedGeneratedAt,
                ValidateOnly: false));

            result.Mode.Should().Be(FailedFinalizeContinuityPromotionService.ModeValidateOnly);
            result.Status.Should().Be("accepted");
            result.WrittenFiles.Should().BeEmpty();
            result.CheckedFiles.Should().BeEmpty();
            Directory.Exists(result.PackageRoot).Should().BeFalse();
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void PromotionService_PackageThenCheckOnly_ValidatesDeterministicOutputs()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var paths = CreatePromotionPaths(tempRoot);
            var service = new FailedFinalizeContinuityPromotionService();

            var packageResult = service.Promote(new(
                paths,
                FailedFinalizeContinuityPromotionService.ModePackage,
                SourceInput: null,
                OutputRoot: null,
                GeneratedAt: FixedGeneratedAt,
                ValidateOnly: false));
            var checkResult = service.Promote(new(
                paths,
                FailedFinalizeContinuityPromotionService.ModeCheckOnly,
                SourceInput: null,
                OutputRoot: null,
                GeneratedAt: null,
                ValidateOnly: false));

            packageResult.WrittenFiles.Should().HaveCount(FailedFinalizeContinuityArtifactGenerator.RequiredArtifactPaths.Length);
            checkResult.CheckedFiles.Should().HaveCount(FailedFinalizeContinuityArtifactGenerator.RequiredArtifactPaths.Length);
            packageResult.GeneratedPackage.Artifacts.Select(artifact => artifact.Sha256Hash)
                .Should()
                .Equal(checkResult.GeneratedPackage.Artifacts.Select(artifact => artifact.Sha256Hash));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void PromotionService_CheckOnlyDetectsTamperedOutput()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var paths = CreatePromotionPaths(tempRoot);
            var service = new FailedFinalizeContinuityPromotionService();
            var packageResult = service.Promote(new(
                paths,
                FailedFinalizeContinuityPromotionService.ModePackage,
                SourceInput: null,
                OutputRoot: null,
                GeneratedAt: FixedGeneratedAt,
                ValidateOnly: false));
            var publicSummaryPath = Path.Combine(
                packageResult.PackageRoot,
                FailedFinalizeContinuityArtifactGenerator.PublicSafeSummaryPath);
            File.AppendAllText(publicSummaryPath, "tampered");

            var act = () => service.Promote(new(
                paths,
                FailedFinalizeContinuityPromotionService.ModeCheckOnly,
                SourceInput: null,
                OutputRoot: null,
                GeneratedAt: null,
                ValidateOnly: false));

            act.Should()
                .Throw<FailedFinalizeContinuityPromotionException>()
                .Where(ex => ex.Details.Any(detail => detail.Contains("Hash mismatch", StringComparison.Ordinal)));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void ArtifactGenerator_DownstreamHandoffContainsFeat156Inputs()
    {
        var source = FailedFinalizeContinuityContracts.ReadJsonObject(
            Path.Combine(SourceRoot, "failed-finalize-continuity-source.json"));

        var package = FailedFinalizeContinuityArtifactGenerator.GenerateFromSource(source, FixedGeneratedAt);

        package.Status.Should().Be("accepted");
        var handoffArtifact = package.Artifacts.Should()
            .ContainSingle(artifact => artifact.RelativePath == FailedFinalizeContinuityArtifactGenerator.DownstreamHandoffPath)
            .Subject;
        var handoff = JsonNode.Parse(handoffArtifact.Content)!.AsObject();
        FailedFinalizeContinuityContracts.GetString(handoff, "status").Should().Be("accepted");
        FailedFinalizeContinuityContracts.GetBool(handoff, "directRegisterMutation", fallback: true).Should().BeFalse();
        FailedFinalizeContinuityContracts.GetStringArray(handoff, "consumers").Should().Contain(["FEAT-148", "FEAT-156"]);
        FailedFinalizeContinuityContracts.GetString(handoff, "scoreProposalPath")
            .Should()
            .Be(FailedFinalizeContinuityArtifactGenerator.ScoreProposalPath);
        FailedFinalizeContinuityContracts.GetString(handoff, "readinessFragmentPath")
            .Should()
            .Be(FailedFinalizeContinuityArtifactGenerator.ReadinessFragmentPath);
        handoff["failedFinalizeBlockerClearance"]!.AsObject()["blockersCleared"]!.AsArray()
            .Select(item => item!.GetValue<string>())
            .Should()
            .Contain("FEAT139-GOVERNED-FAILED-FINALIZE-MISSING");
    }

    private static DateTimeOffset FixedGeneratedAt =>
        DateTimeOffset.Parse("2026-05-31T00:00:00Z");

    private static FailedFinalizeContinuityPromotionPaths CreatePromotionPaths(string tempRoot) =>
        new(
            tempRoot,
            FixtureRoot,
            Path.Combine(FixtureRoot, "schemas"),
            Path.Combine(FixtureRoot, "examples"),
            Path.Combine(SourceRoot, "failed-finalize-continuity-source.json"),
            Path.Combine(tempRoot, "Failed-Finalize-Continuity-Rehearsal"));

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "hush-feat155-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempRoot(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string FixtureRoot =>
        Path.Combine(
            HushVotingReadinessTestArtifacts.ServerNodeRoot,
            "Node",
            "HushServerNode.Tests",
            "Fixtures",
            "HushVotingReadiness",
            "Failed-Finalize-Continuity-Rehearsal");

    private static string SourceRoot => Path.Combine(FixtureRoot, "examples", "release-baseline");
}
