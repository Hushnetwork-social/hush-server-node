using FluentAssertions;
using ProductionLikeOperationalRunPromoter;
using Xunit;
using static HushServerNode.Tests.Elections.ProductionLikeOperationalRunTestHelpers;

namespace HushServerNode.Tests.Elections;

public sealed class ProductionLikeOperationalRunPromotionServiceTests
{
    [Fact]
    public void Promote_WithValidateOnly_DoesNotCreatePackageDirectory()
    {
        // Arrange
        var paths = CreatePaths();
        var outputRoot = CreateOutputRoot(paths);
        var service = new ProductionLikeOperationalRunPromotionService();

        try
        {
            // Act
            var result = service.Promote(new(
                paths,
                ProductionLikeOperationalRunPromotionService.ModePackage,
                null,
                outputRoot,
                FixedGeneratedAt(),
                ValidateOnly: true));

            // Assert
            result.Mode.Should().Be(ProductionLikeOperationalRunPromotionService.ModeValidateOnly);
            result.WrittenFiles.Should().BeEmpty();
            Directory.Exists(Path.Combine(outputRoot, "package")).Should().BeFalse();
        }
        finally
        {
            DeleteOutputRoot(outputRoot);
        }
    }

    [Fact]
    public void Promote_WithPackageMode_WritesExpectedArtifactSet()
    {
        // Arrange
        var paths = CreatePaths();
        var outputRoot = CreateOutputRoot(paths);
        var service = new ProductionLikeOperationalRunPromotionService();

        try
        {
            // Act
            var result = service.Promote(new(
                paths,
                ProductionLikeOperationalRunPromotionService.ModePackage,
                null,
                outputRoot,
                FixedGeneratedAt(),
                ValidateOnly: false));

            // Assert
            result.Status.Should().Be("accepted");
            result.PackageRoot.Should().Be(Path.Combine(outputRoot, "package"));
            result.WrittenFiles.Should().HaveCount(ProductionLikeOperationalRunArtifactGenerator.RequiredArtifactPaths.Length);
            result.WrittenFiles.Should().OnlyContain(path => File.Exists(path));
            File.Exists(Path.Combine(result.PackageRoot, ProductionLikeOperationalRunArtifactGenerator.ManifestPath)).Should().BeTrue();
        }
        finally
        {
            DeleteOutputRoot(outputRoot);
        }
    }

    [Fact]
    public void Promote_WithCheckOnly_ValidatesExistingPackageWithoutWriting()
    {
        // Arrange
        var paths = CreatePaths();
        var outputRoot = CreateOutputRoot(paths);
        var service = new ProductionLikeOperationalRunPromotionService();

        try
        {
            service.Promote(new(
                paths,
                ProductionLikeOperationalRunPromotionService.ModePackage,
                null,
                outputRoot,
                FixedGeneratedAt(),
                ValidateOnly: false));

            // Act
            var result = service.Promote(new(
                paths,
                ProductionLikeOperationalRunPromotionService.ModeCheckOnly,
                null,
                outputRoot,
                FixedGeneratedAt(),
                ValidateOnly: false));

            // Assert
            result.Mode.Should().Be(ProductionLikeOperationalRunPromotionService.ModeCheckOnly);
            result.WrittenFiles.Should().BeEmpty();
            result.CheckedFiles.Should().HaveCount(ProductionLikeOperationalRunArtifactGenerator.RequiredArtifactPaths.Length);
        }
        finally
        {
            DeleteOutputRoot(outputRoot);
        }
    }

    [Fact]
    public void Promote_WithCheckOnlyAndNoGeneratedAt_UsesExistingManifestTimestamp()
    {
        // Arrange
        var paths = CreatePaths();
        var outputRoot = CreateOutputRoot(paths);
        var service = new ProductionLikeOperationalRunPromotionService();

        try
        {
            service.Promote(new(
                paths,
                ProductionLikeOperationalRunPromotionService.ModePackage,
                null,
                outputRoot,
                FixedGeneratedAt(),
                ValidateOnly: false));

            // Act
            var result = service.Promote(new(
                paths,
                ProductionLikeOperationalRunPromotionService.ModeCheckOnly,
                null,
                outputRoot,
                GeneratedAt: null,
                ValidateOnly: false));

            // Assert
            result.CheckedFiles.Should().HaveCount(ProductionLikeOperationalRunArtifactGenerator.RequiredArtifactPaths.Length);
        }
        finally
        {
            DeleteOutputRoot(outputRoot);
        }
    }

    [Fact]
    public void Promote_WithCheckOnlyHashMismatch_Fails()
    {
        // Arrange
        var paths = CreatePaths();
        var outputRoot = CreateOutputRoot(paths);
        var service = new ProductionLikeOperationalRunPromotionService();

        try
        {
            var packageResult = service.Promote(new(
                paths,
                ProductionLikeOperationalRunPromotionService.ModePackage,
                null,
                outputRoot,
                FixedGeneratedAt(),
                ValidateOnly: false));
            File.AppendAllText(
                Path.Combine(packageResult.PackageRoot, ProductionLikeOperationalRunArtifactGenerator.PublicSafeSummaryPath),
                "tampered");

            // Act
            var act = () => service.Promote(new(
                paths,
                ProductionLikeOperationalRunPromotionService.ModeCheckOnly,
                null,
                outputRoot,
                FixedGeneratedAt(),
                ValidateOnly: false));

            // Assert
            act.Should().Throw<ProductionLikeOperationalRunPromotionException>()
                .Where(ex => ex.Errors.Any(error => error.Contains("Hash mismatch", StringComparison.Ordinal)));
        }
        finally
        {
            DeleteOutputRoot(outputRoot);
        }
    }

    [Fact]
    public void Promote_WithOutputRootOutsideWorkspace_FailsBeforeWriting()
    {
        // Arrange
        var paths = CreatePaths();
        var outputRoot = Path.Combine(Path.GetTempPath(), $"feat154-outside-output-{Guid.NewGuid():N}");
        var service = new ProductionLikeOperationalRunPromotionService();

        // Act
        var act = () => service.Promote(new(
            paths,
            ProductionLikeOperationalRunPromotionService.ModePackage,
            null,
            outputRoot,
            FixedGeneratedAt(),
            ValidateOnly: false));

        // Assert
        act.Should().Throw<ProductionLikeOperationalRunPromotionException>()
            .Where(ex => ex.Errors.Any(error => error.Contains("output root", StringComparison.Ordinal)));
        Directory.Exists(outputRoot).Should().BeFalse();
    }

    [Fact]
    public void Promote_WithSourceInputOutsideSourceRoot_FailsBeforeWriting()
    {
        // Arrange
        var paths = CreatePaths();
        var outputRoot = CreateOutputRoot(paths);
        var sourceInput = Path.GetTempFileName();
        var service = new ProductionLikeOperationalRunPromotionService();

        try
        {
            // Act
            var act = () => service.Promote(new(
                paths,
                ProductionLikeOperationalRunPromotionService.ModePackage,
                sourceInput,
                outputRoot,
                FixedGeneratedAt(),
                ValidateOnly: false));

            // Assert
            act.Should().Throw<ProductionLikeOperationalRunPromotionException>()
                .Where(ex => ex.Errors.Any(error => error.Contains("source", StringComparison.Ordinal)));
            Directory.Exists(Path.Combine(outputRoot, "package")).Should().BeFalse();
        }
        finally
        {
            File.Delete(sourceInput);
            DeleteOutputRoot(outputRoot);
        }
    }

    [Fact]
    public void PromotionPaths_FromWorkspaceRoot_UsesStableHushDocumentsPackageRoot()
    {
        // Arrange
        var paths = CreatePaths();

        // Act
        var expected = Path.Combine(
            paths.WorkspaceRoot,
            "hush-documents",
            "PrivateServer_ElectronicVoting",
            ProductionLikeOperationalRunPromotionPaths.OutputFolder);

        // Assert
        paths.OutputRoot.Should().Be(expected);
    }

    private static string CreateOutputRoot(ProductionLikeOperationalRunPromotionPaths paths)
    {
        var outputRoot = Path.Combine(paths.WorkspaceRoot, ".test-output", $"feat154-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputRoot);
        return outputRoot;
    }

    private static void DeleteOutputRoot(string outputRoot)
    {
        if (Directory.Exists(outputRoot))
        {
            Directory.Delete(outputRoot, recursive: true);
        }

        var parent = Directory.GetParent(outputRoot);
        if (parent is not null &&
            parent.Name == ".test-output" &&
            Directory.Exists(parent.FullName) &&
            !Directory.EnumerateFileSystemEntries(parent.FullName).Any())
        {
            Directory.Delete(parent.FullName);
        }
    }

    private static DateTimeOffset FixedGeneratedAt() =>
        DateTimeOffset.Parse("2026-05-28T12:00:00Z");
}
