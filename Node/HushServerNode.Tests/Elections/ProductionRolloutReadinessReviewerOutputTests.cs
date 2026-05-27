using System.Text.Json.Nodes;
using FluentAssertions;
using ProductionRolloutReadinessPromoter;
using static HushServerNode.Tests.Elections.ProductionRolloutReadinessTestHelpers;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ProductionRolloutReadinessReviewerOutputTests
{
    [Fact]
    public void PublicOutputChecks_WithPublicElectionOverclaim_BlockPackage()
    {
        // Arrange
        var paths = CreatePaths();
        var source = LoadExample("amber-ready");
        source["claimPolicy"]!.AsObject()["allowedWithLimitationsWording"] =
            "HushVoting is public election ready for private organizations.";
        var sourceInput = WriteSourceExample(paths, source, "public-election-overclaim");

        // Act
        var package = ProductionRolloutReadinessArtifactGenerator.Generate(
            paths,
            sourceInput,
            DateTimeOffset.Parse("2026-05-27T12:00:00Z"));
        var checkResults = ReadArtifactJson(package, ProductionRolloutReadinessArtifactGenerator.CheckResultsPath);
        var evidencePackage = ReadArtifactJson(package, ProductionRolloutReadinessArtifactGenerator.EvidencePackagePath);

        // Assert
        package.Status.Should().Be("blocked");
        package.PublicOutputFindings.Should().Contain(finding =>
            finding.Category == "overclaim" &&
            finding.Evidence == "public election ready");
        checkResults["publicOutputFindings"]!.AsArray().Should().NotBeEmpty();
        evidencePackage["gateResult"]!.AsObject()["publicOutputFindings"]!.AsArray().Should().NotBeEmpty();
    }

    [Fact]
    public void PublicOutputChecks_WithRestrictedBodyLeakage_BlockPackage()
    {
        // Arrange
        var paths = CreatePaths();
        var source = LoadExample("amber-ready");
        source["claimPolicy"]!.AsObject()["allowedWithLimitationsWording"] =
            "Reviewer summary leaked voter identity and vote choice body content.";
        var sourceInput = WriteSourceExample(paths, source, "restricted-body-leakage");

        // Act
        var package = ProductionRolloutReadinessArtifactGenerator.Generate(
            paths,
            sourceInput,
            DateTimeOffset.Parse("2026-05-27T12:00:00Z"));

        // Assert
        package.Status.Should().Be("blocked");
        package.PublicOutputFindings.Should().Contain(finding =>
            finding.Category == "restricted_material" &&
            finding.Evidence == "voter identity");
    }

    [Fact]
    public void RestrictedReviewerIndex_DoesNotCopyRestrictedPayloadBodies()
    {
        // Arrange
        var paths = CreatePaths();

        // Act
        var package = ProductionRolloutReadinessArtifactGenerator.Generate(
            paths,
            generatedAt: DateTimeOffset.Parse("2026-05-27T12:00:00Z"));
        var restrictedIndex = ReadArtifactJson(package, ProductionRolloutReadinessArtifactGenerator.RestrictedReviewerIndexPath);

        // Assert
        restrictedIndex["restrictedPayloadsExcluded"]!.GetValue<bool>().Should().BeTrue();
        restrictedIndex["restrictedRefs"]!.AsArray()
            .OfType<JsonObject>()
            .Should().OnlyContain(item =>
                item["payloadCopied"] != null &&
                item["payloadCopied"]!.GetValue<bool>() == false &&
                !item.ContainsKey("restrictedBody") &&
                !item.ContainsKey("content"));
    }
}
