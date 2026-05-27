using System.Text.Json.Nodes;
using FluentAssertions;
using ProductionRolloutReadinessPromoter;
using static HushServerNode.Tests.Elections.ProductionRolloutReadinessTestHelpers;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ProductionRolloutReadinessPromotionServiceTests
{
    [Fact]
    public void Promote_WithValidateOnly_DoesNotCreatePackageDirectory()
    {
        // Arrange
        var paths = CreatePaths();
        var outputRoot = CreateOutputRoot();
        var service = new ProductionRolloutReadinessPromotionService();

        // Act
        var result = service.Promote(new(
            paths,
            ProductionRolloutReadinessPromotionService.ModePackage,
            null,
            outputRoot,
            DateTimeOffset.Parse("2026-05-27T12:00:00Z"),
            ValidateOnly: true));

        // Assert
        result.Mode.Should().Be(ProductionRolloutReadinessPromotionService.ModeValidateOnly);
        result.WrittenFiles.Should().BeEmpty();
        Directory.Exists(Path.Combine(outputRoot, "package")).Should().BeFalse();
    }

    [Fact]
    public void Promote_WithPackageMode_WritesExpectedArtifactSet()
    {
        // Arrange
        var paths = CreatePaths();
        var outputRoot = CreateOutputRoot();
        var service = new ProductionRolloutReadinessPromotionService();

        // Act
        var result = service.Promote(new(
            paths,
            ProductionRolloutReadinessPromotionService.ModePackage,
            null,
            outputRoot,
            DateTimeOffset.Parse("2026-05-27T12:00:00Z"),
            ValidateOnly: false));
        var checkResultsPath = Path.Combine(result.PackageRoot, ProductionRolloutReadinessArtifactGenerator.CheckResultsPath);
        var checkResults = JsonNode.Parse(File.ReadAllText(checkResultsPath))!.AsObject();

        // Assert
        result.Status.Should().Be("blocked");
        result.WrittenFiles.Should().HaveCount(ProductionRolloutReadinessArtifactGenerator.RequiredArtifactPaths.Length);
        result.WrittenFiles.Should().OnlyContain(path => File.Exists(path));
        checkResults["status"]!.GetValue<string>().Should().Be("blocked");
    }

    [Fact]
    public void Promote_WithCheckOnly_ValidatesExistingPackageWithoutWriting()
    {
        // Arrange
        var paths = CreatePaths();
        var outputRoot = CreateOutputRoot();
        var service = new ProductionRolloutReadinessPromotionService();
        service.Promote(new(
            paths,
            ProductionRolloutReadinessPromotionService.ModePackage,
            null,
            outputRoot,
            DateTimeOffset.Parse("2026-05-27T12:00:00Z"),
            ValidateOnly: false));

        // Act
        var result = service.Promote(new(
            paths,
            ProductionRolloutReadinessPromotionService.ModeCheckOnly,
            null,
            outputRoot,
            DateTimeOffset.Parse("2026-05-27T12:00:00Z"),
            ValidateOnly: false));

        // Assert
        result.Mode.Should().Be(ProductionRolloutReadinessPromotionService.ModeCheckOnly);
        result.WrittenFiles.Should().BeEmpty();
        result.CheckedFiles.Should().HaveCount(ProductionRolloutReadinessArtifactGenerator.RequiredArtifactPaths.Length);
    }

    [Fact]
    public void Promote_WithCheckOnlyMissingPackage_Fails()
    {
        // Arrange
        var paths = CreatePaths();
        var outputRoot = CreateOutputRoot();
        var service = new ProductionRolloutReadinessPromotionService();

        // Act
        var act = () => service.Promote(new(
            paths,
            ProductionRolloutReadinessPromotionService.ModeCheckOnly,
            null,
            outputRoot,
            DateTimeOffset.Parse("2026-05-27T12:00:00Z"),
            ValidateOnly: false));

        // Assert
        act.Should().Throw<ProductionRolloutReadinessPromotionException>()
            .Where(ex => ex.Details.Any(detail => detail.Contains("Package root does not exist", StringComparison.Ordinal)));
    }

    [Fact]
    public void Promote_WithCheckOnlyHashMismatch_Fails()
    {
        // Arrange
        var paths = CreatePaths();
        var outputRoot = CreateOutputRoot();
        var service = new ProductionRolloutReadinessPromotionService();
        var packageResult = service.Promote(new(
            paths,
            ProductionRolloutReadinessPromotionService.ModePackage,
            null,
            outputRoot,
            DateTimeOffset.Parse("2026-05-27T12:00:00Z"),
            ValidateOnly: false));
        File.AppendAllText(
            Path.Combine(packageResult.PackageRoot, ProductionRolloutReadinessArtifactGenerator.PublicSafeSummaryPath),
            "tampered");

        // Act
        var act = () => service.Promote(new(
            paths,
            ProductionRolloutReadinessPromotionService.ModeCheckOnly,
            null,
            outputRoot,
            DateTimeOffset.Parse("2026-05-27T12:00:00Z"),
            ValidateOnly: false));

        // Assert
        act.Should().Throw<ProductionRolloutReadinessPromotionException>()
            .Where(ex => ex.Details.Any(detail => detail.Contains("Hash mismatch", StringComparison.Ordinal)));
    }

    private static string CreateOutputRoot()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), $"feat148-production-rollout-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputRoot);
        return outputRoot;
    }
}
