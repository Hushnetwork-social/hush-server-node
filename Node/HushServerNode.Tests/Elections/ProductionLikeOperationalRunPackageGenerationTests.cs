using System.Text.Json.Nodes;
using FluentAssertions;
using ProductionLikeOperationalRunPromoter;
using Xunit;
using static HushServerNode.Tests.Elections.ProductionLikeOperationalRunTestHelpers;

namespace HushServerNode.Tests.Elections;

public sealed class ProductionLikeOperationalRunPackageGenerationTests
{
    [Fact]
    public void AcceptedSource_GeneratesAllRequiredArtifactsAndScoreProposal()
    {
        // Arrange
        var source = LoadBaseline();

        // Act
        var package = ProductionLikeOperationalRunArtifactGenerator.GenerateFromSource(source, FixedGeneratedAt());

        // Assert
        package.Status.Should().Be("accepted");
        package.Artifacts.Select(artifact => artifact.RelativePath)
            .Should()
            .BeEquivalentTo(ProductionLikeOperationalRunArtifactGenerator.RequiredArtifactPaths);

        var scoreProposal = ReadArtifactJson(package, ProductionLikeOperationalRunArtifactGenerator.ScoreProposalPath);
        scoreProposal["dimensionId"]!.GetValue<string>().Should().Be("RDY-DIM-007");
        scoreProposal["proposedScoreFrom"]!.GetValue<int>().Should().Be(6);
        scoreProposal["proposedScoreTo"]!.GetValue<int>().Should().Be(8);
        scoreProposal["scoreChangeAllowed"]!.GetValue<bool>().Should().BeTrue();
        scoreProposal["doesNotMutateRegister"]!.GetValue<bool>().Should().BeTrue();
        scoreProposal["directRegisterMutation"]!.GetValue<bool>().Should().BeFalse();

        var readiness = ReadArtifactJson(package, ProductionLikeOperationalRunArtifactGenerator.ReadinessFragmentPath);
        readiness["status"]!.GetValue<string>().Should().Be("accepted");
        readiness["scoreEffect"]!.AsObject()["scoreChangeAllowed"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void AcceptedSource_WithFixedTimestamp_GeneratesDeterministicHashes()
    {
        // Arrange
        var firstSource = LoadBaseline();
        var secondSource = LoadBaseline();

        // Act
        var first = ProductionLikeOperationalRunArtifactGenerator.GenerateFromSource(firstSource, FixedGeneratedAt());
        var second = ProductionLikeOperationalRunArtifactGenerator.GenerateFromSource(secondSource, FixedGeneratedAt());

        // Assert
        first.Artifacts
            .Select(artifact => (artifact.RelativePath, artifact.Sha256Hash, artifact.Content))
            .Should()
            .Equal(second.Artifacts.Select(artifact => (artifact.RelativePath, artifact.Sha256Hash, artifact.Content)));
    }

    [Fact]
    public void Manifest_ExcludesItsOwnMutableHashFromArtifactList()
    {
        // Arrange
        var source = LoadBaseline();

        // Act
        var package = ProductionLikeOperationalRunArtifactGenerator.GenerateFromSource(source, FixedGeneratedAt());
        var manifest = ReadArtifactJson(package, ProductionLikeOperationalRunArtifactGenerator.ManifestPath);

        // Assert
        manifest["artifacts"]!
            .AsArray()
            .Select(item => item!.AsObject()["path"]!.GetValue<string>())
            .Should()
            .NotContain(ProductionLikeOperationalRunArtifactGenerator.ManifestPath);
    }

    [Fact]
    public void BlockedSource_EmitsSafeDiagnosticsAndNoScoreMovement()
    {
        // Arrange
        var source = LoadSourceForCategory("missing_deployment_proof");

        // Act
        var package = ProductionLikeOperationalRunArtifactGenerator.GenerateFromSource(source, FixedGeneratedAt());

        // Assert
        package.Status.Should().Be("blocked");
        package.GateEvaluation.Diagnostics.Should().Contain("FEAT154-DEPLOYMENT-PROOF-MISSING");
        package.PublicOutputFindings.Should().BeEmpty();

        var scoreProposal = ReadArtifactJson(package, ProductionLikeOperationalRunArtifactGenerator.ScoreProposalPath);
        scoreProposal["scoreChangeAllowed"]!.GetValue<bool>().Should().BeFalse();
        scoreProposal["proposedScoreFrom"]!.GetValue<int>().Should().Be(6);
        scoreProposal["proposedScoreTo"]!.GetValue<int>().Should().Be(6);
        scoreProposal["blockedBy"]!.AsArray()
            .Select(item => item!.GetValue<string>())
            .Should()
            .Contain("FEAT154-DEPLOYMENT-PROOF-MISSING");

        var publicSummary = ReadArtifact(package, ProductionLikeOperationalRunArtifactGenerator.PublicSafeSummaryPath);
        foreach (var needle in ProductionLikeOperationalRunGateChecker.ForbiddenPublicMaterialNeedles)
        {
            publicSummary.Contains(needle, StringComparison.OrdinalIgnoreCase)
                .Should()
                .BeFalse($"public output must not contain {needle}");
        }
    }

    private static JsonObject ReadArtifactJson(
        ProductionLikeOperationalRunGeneratedPackage package,
        string relativePath) =>
        JsonNode.Parse(ReadArtifact(package, relativePath))!.AsObject();

    private static string ReadArtifact(
        ProductionLikeOperationalRunGeneratedPackage package,
        string relativePath) =>
        package.Artifacts.Single(artifact => artifact.RelativePath == relativePath).Content;

    private static DateTimeOffset FixedGeneratedAt() =>
        DateTimeOffset.Parse("2026-05-28T12:00:00Z");
}
