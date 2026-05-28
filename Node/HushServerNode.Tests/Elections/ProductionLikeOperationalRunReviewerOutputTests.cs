using System.Text.Json.Nodes;
using FluentAssertions;
using ProductionLikeOperationalRunPromoter;
using Xunit;
using static HushServerNode.Tests.Elections.ProductionLikeOperationalRunTestHelpers;

namespace HushServerNode.Tests.Elections;

public sealed class ProductionLikeOperationalRunReviewerOutputTests
{
    [Fact]
    public void AcceptedSource_PublicSafeSummaryStatesReviewerBoundariesWithoutFindings()
    {
        // Arrange
        var source = LoadBaseline();

        // Act
        var package = ProductionLikeOperationalRunArtifactGenerator.GenerateFromSource(source, FixedGeneratedAt());

        // Assert
        package.PublicOutputFindings.Should().BeEmpty();

        var summary = ReadArtifact(package, ProductionLikeOperationalRunArtifactGenerator.PublicSafeSummaryPath);
        summary.Should().Contain("## Reviewer Summary");
        summary.Should().Contain("controlled production-like HushVoting operational run package");
        summary.Should().Contain("Register effect: score proposal input only; no direct readiness-register mutation.");
        summary.Should().Contain("This package does not claim production rollout readiness.");
        summary.Should().Contain("This package does not claim public/state election readiness.");
        summary.Should().Contain("This package does not claim legal sufficiency.");
        summary.Should().Contain("This package does not claim certification or external validation.");
        summary.Should().Contain("This package does not claim failed-finalize continuity completion; FEAT-155 owns that proof.");

        var scanResult = ReadArtifactJson(package, ProductionLikeOperationalRunArtifactGenerator.NoSecretScanResultPath);
        scanResult["status"]!.GetValue<string>().Should().Be("passed");
        scanResult["findings"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public void RestrictedReviewerIndex_ReferencesPrivateEvidenceWithoutCopyingPayloadFields()
    {
        // Arrange
        var source = LoadBaseline();

        // Act
        var package = ProductionLikeOperationalRunArtifactGenerator.GenerateFromSource(source, FixedGeneratedAt());

        // Assert
        var index = ReadArtifact(package, ProductionLikeOperationalRunArtifactGenerator.RestrictedEvidenceIndexPath);
        index.Should().StartWith("# Restricted Reviewer Index");
        index.Should().Contain("references and hashes only");
        index.Should().Contain("| Ref | Visibility | Path Ref | Hash | Payload Copied |");
        index.Should().Contain("restricted-evidence/feat-132/deployment-proof-set.json");
        index.Should().Contain("| false |");
        index.Should().NotContain("publicRef");
        index.Should().NotContain("claimEffect");
        index.Should().NotContain("Synthetic election and participant data only");
        index.Should().NotContain("operator contact");
        index.Should().NotContain("private key");
        index.Should().NotContain("raw log");
    }

    [Fact]
    public void PublicOutputScan_FlagsForbiddenMaterialAndOverclaimPhrases()
    {
        // Arrange
        var source = LoadBaseline();
        const string unsafePublicOutput = """
            Production rollout ready.
            The package is legally sufficient and externally validated.
            Failed finalize continuity complete.
            voter data: available
            private key: 123
            private URL: https://private.example.invalid/run
            local path: C:\ops\run.log
            support case data: copied
            raw log: copied
            """;

        // Act
        var findings = ProductionLikeOperationalRunContracts.ScanPublicOutput(
            source,
            [("unsafe-public-summary.md", unsafePublicOutput)]);

        // Assert
        findings.Where(finding => finding.Category == "overclaim")
            .Select(finding => finding.Evidence)
            .Should()
            .Contain(["production rollout ready", "legally sufficient", "externally validated", "failed finalize continuity complete"]);

        findings.Where(finding => finding.Category == "restricted_material")
            .Select(finding => finding.Evidence)
            .Should()
            .Contain(["voter data", "private key", "private url", "local path", "support case data", "raw log"]);
    }

    [Fact]
    public void PublicOutputScan_AllowsExplicitNonClaimWording()
    {
        // Arrange
        var source = LoadBaseline();
        const string explicitNonClaims = """
            - This package does not claim production rollout readiness.
            - This package does not claim public/state election readiness.
            - This package does not claim legal sufficiency.
            - This package does not claim certification or external validation.
            - This package does not claim failed-finalize continuity completion.
            """;

        // Act
        var findings = ProductionLikeOperationalRunContracts.ScanPublicOutput(
            source,
            [("safe-public-summary.md", explicitNonClaims)]);

        // Assert
        findings.Should().BeEmpty();
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
