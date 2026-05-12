using System.Text.Json;
using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ProtocolPackagePromotionServiceTests : IDisposable
{
    private static readonly DateTime FixedGeneratedAt = new(2026, 5, 4, 21, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _tempRoot;

    public ProtocolPackagePromotionServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"hush-feat-112-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void Promote_WithSameInputAndVersion_ProducesStableHashesAndCatalogMetadata()
    {
        var paths = CreatePaths();
        WriteCompleteSourcePackage(paths.WorkingSourceRoot);
        var options = CreateOptions(paths);
        var service = new ProtocolPackagePromotionService();

        var first = service.Promote(options);
        var second = service.Promote(options);

        second.SpecificationManifest.PackageHash.Should().Be(first.SpecificationManifest.PackageHash);
        second.ProofManifest.PackageHash.Should().Be(first.ProofManifest.PackageHash);
        second.ReleaseManifest.ReleaseManifestHash.Should().Be(first.ReleaseManifest.ReleaseManifestHash);
        second.CatalogEntry.ReleaseManifestHash.Should().Be(first.CatalogEntry.ReleaseManifestHash);
        second.CatalogEntry.IsLatestForCompatibleProfiles.Should().BeTrue();
        second.CatalogEntry.ApprovalStatus.Should().Be(ProtocolPackageApprovalStatus.ApprovedInternal);
        second.CatalogEntry.CompatibleProfileIds.Should().Contain(ElectionSelectableProfileCatalog.AdminOnlyProductionProfileId);

        second.SpecificationManifest.Files.Should().HaveCount(ProtocolPackagePromotionService.RequiredSpecificationFiles.Count);
        second.ProofManifest.Files.Should().HaveCount(ProtocolPackagePromotionService.RequiredProofFiles.Count);
        second.ReleaseManifest.ReleaseFiles.Should().HaveCount(ProtocolPackagePromotionService.RequiredReleaseFiles.Count);
        second.ReleaseManifest.ReleaseFiles.Should().ContainSingle(x => x.RelativePath == "ChangeLog.md");
        second.SpecificationManifest.Files.Should().OnlyContain(x => x.Sha256Hash.Length == 64 && x.SizeBytes > 0);
        second.ProofManifest.Files.Should().OnlyContain(x => x.Sha256Hash.Length == 64 && x.SizeBytes > 0);
        second.ReleaseManifest.ReleaseFiles.Should().OnlyContain(x => x.Sha256Hash.Length == 64 && x.SizeBytes > 0);

        File.Exists(Path.Combine(
            paths.OfficialArtifactsRoot,
            "v1.0.0",
            "ChangeLog.md")).Should().BeTrue();
        File.Exists(Path.Combine(
            paths.OfficialArtifactsRoot,
            "v1.0.0",
            ProtocolPackagePromotionService.SpecificationPackageFolderName,
            "ChangeLog.md")).Should().BeFalse();
        File.Exists(Path.Combine(
            paths.OfficialArtifactsRoot,
            "v1.0.0",
            ProtocolPackagePromotionService.SpecificationPackageFolderName,
            $"{ProtocolPackagePromotionService.SpecificationPackageFolderName}.zip")).Should().BeTrue();
        File.Exists(Path.Combine(
            paths.OfficialArtifactsRoot,
            "v1.0.0",
            ProtocolPackagePromotionService.ProofPackageFolderName,
            $"{ProtocolPackagePromotionService.ProofPackageFolderName}.zip")).Should().BeTrue();
        File.Exists(Path.Combine(
            paths.WebsitePublicArtifactsRoot!,
            "v1.0.0",
            ProtocolPackagePromotionService.SpecificationPackageFolderName,
            $"{ProtocolPackagePromotionService.SpecificationPackageFolderName}.zip")).Should().BeTrue();
        File.Exists(Path.Combine(
            paths.WebsitePublicArtifactsRoot!,
            "v1.0.0",
            ProtocolPackagePromotionService.ProofPackageFolderName,
            $"{ProtocolPackagePromotionService.ProofPackageFolderName}.zip")).Should().BeTrue();
        File.Exists(Path.Combine(
            paths.PublicPackageRepositoryArtifactsRoot!,
            "v1.0.0",
            ProtocolPackagePromotionService.SpecificationPackageFolderName,
            $"{ProtocolPackagePromotionService.SpecificationPackageFolderName}.zip")).Should().BeTrue();
        File.Exists(Path.Combine(
            paths.PublicPackageRepositoryArtifactsRoot!,
            "v1.0.0",
            ProtocolPackagePromotionService.ProofPackageFolderName,
            $"{ProtocolPackagePromotionService.ProofPackageFolderName}.zip")).Should().BeTrue();
        File.Exists(paths.ServerCatalogPath).Should().BeTrue();

        var catalog = JsonSerializer.Deserialize<ApprovedProtocolPackageCatalogEntryRecord[]>(
            File.ReadAllText(paths.ServerCatalogPath),
            JsonOptions);

        catalog.Should().ContainSingle();
        catalog![0].PackageVersion.Should().Be("v1.0.0");
        catalog[0].SpecPackageHash.Should().Be(second.SpecificationManifest.PackageHash);
        catalog[0].ProofPackageHash.Should().Be(second.ProofManifest.PackageHash);
        catalog[0].ReleaseManifestHash.Should().Be(second.ReleaseManifest.ReleaseManifestHash);
        catalog[0].SpecAccessLocations.Should().ContainSingle(x =>
            x.LocationKind == ProtocolPackageAccessLocationKind.PublicWebsite &&
            x.ContentHash == second.SpecificationManifest.PackageHash &&
            x.Location.Contains("/v1.0.0/Protocol-Specification-Package/", StringComparison.Ordinal));
        catalog[0].ProofAccessLocations.Should().ContainSingle(x =>
            x.LocationKind == ProtocolPackageAccessLocationKind.PublicWebsite &&
            x.ContentHash == second.ProofManifest.PackageHash &&
            x.Location.Contains("/v1.0.0/Protocol-Proof-And-Crypto-Review/", StringComparison.Ordinal));
    }

    [Fact]
    public void Promote_WithExistingCatalog_PreservesOlderVersionsAndMarksPromotedEntryLatest()
    {
        var paths = CreatePaths();
        WriteCompleteSourcePackage(paths.WorkingSourceRoot);
        var existingEntry = CreateCatalogEntry("v0.9.0", isLatestForCompatibleProfiles: true);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.ServerCatalogPath)!);
        File.WriteAllText(paths.ServerCatalogPath, JsonSerializer.Serialize(new[] { existingEntry }, JsonOptions));

        var result = new ProtocolPackagePromotionService().Promote(CreateOptions(paths));

        var catalog = JsonSerializer.Deserialize<ApprovedProtocolPackageCatalogEntryRecord[]>(
            File.ReadAllText(paths.ServerCatalogPath),
            JsonOptions);

        catalog.Should().HaveCount(2);
        var catalogEntries = catalog!;
        catalogEntries.Single(x => x.PackageVersion == "v0.9.0").IsLatestForCompatibleProfiles.Should().BeFalse();
        catalogEntries.Single(x => x.PackageVersion == "v1.0.0").Should().Match<ApprovedProtocolPackageCatalogEntryRecord>(
            x => x.IsLatestForCompatibleProfiles &&
                 x.ReleaseManifestHash == result.ReleaseManifest.ReleaseManifestHash);
    }

    [Fact]
    public void Promote_WithoutVersion_WithIncompleteSources_DerivesDraftOddMinorBuildAndDoesNotSupersedeApprovedEntry()
    {
        var paths = CreatePaths();
        WriteIncompleteSourcePackage(paths.WorkingSourceRoot);
        var existingEntry = CreateCatalogEntry("v1.0.0", isLatestForCompatibleProfiles: true);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.ServerCatalogPath)!);
        File.WriteAllText(paths.ServerCatalogPath, JsonSerializer.Serialize(new[] { existingEntry }, JsonOptions));

        var result = new ProtocolPackagePromotionService().Promote(CreateAutoOptions(paths));

        result.ReleaseManifest.PackageVersion.Should().Be("v1.1.1");
        result.ReleaseManifest.ApprovalStatus.Should().Be(ProtocolPackageApprovalStatus.DraftPrivate);
        result.CatalogEntry.IsApprovedForElectionOpen.Should().BeFalse();
        result.CatalogEntry.IsLatestForCompatibleProfiles.Should().BeFalse();
        result.IncompleteSourceFiles.Should().NotBeEmpty();

        File.ReadAllText(Path.Combine(
                paths.OfficialArtifactsRoot,
                "v1.1.1",
                ProtocolPackagePromotionService.SpecificationPackageFolderName,
                "README.md"))
            .Should().Contain("Version: v1.1.1")
            .And.NotContain("Version: v1.0.0");
        File.ReadAllText(Path.Combine(
                paths.OfficialArtifactsRoot,
                "v1.1.1",
                ProtocolPackagePromotionService.SpecificationPackageFolderName,
                "Audit-Package-Schema.json"))
            .Should().Contain("\"packageVersion\": \"v1.1.1\"")
            .And.NotContain("\"packageVersion\": \"v1.0.0\"");
        File.ReadAllText(Path.Combine(
                paths.WorkingSourceRoot,
                ProtocolPackagePromotionService.SpecificationPackageFolderName,
                "README.md"))
            .Should().Contain("Version: v1.1.1");

        var catalog = JsonSerializer.Deserialize<ApprovedProtocolPackageCatalogEntryRecord[]>(
            File.ReadAllText(paths.ServerCatalogPath),
            JsonOptions);

        catalog.Should().HaveCount(2);
        var catalogEntries = catalog!;
        catalogEntries.Single(x => x.PackageVersion == "v1.0.0").IsLatestForCompatibleProfiles.Should().BeTrue();
        catalogEntries.Single(x => x.PackageVersion == "v1.1.1").ApprovalStatus.Should().Be(ProtocolPackageApprovalStatus.DraftPrivate);
    }

    [Fact]
    public void Promote_WithoutVersion_WhenExistingApprovedArtifactHasIncompleteMarkers_DowngradesPriorCatalogEntry()
    {
        var paths = CreatePaths();
        WriteIncompleteSourcePackage(paths.WorkingSourceRoot);
        var service = new ProtocolPackagePromotionService();
        service.Promote(CreateOptions(paths, packageVersion: "v1.0.0"));
        var incorrectApprovedEntry = CreateCatalogEntry("v1.0.0", isLatestForCompatibleProfiles: true);
        File.WriteAllText(paths.ServerCatalogPath, JsonSerializer.Serialize(new[] { incorrectApprovedEntry }, JsonOptions));

        service.Promote(CreateAutoOptions(paths));

        var catalog = JsonSerializer.Deserialize<ApprovedProtocolPackageCatalogEntryRecord[]>(
            File.ReadAllText(paths.ServerCatalogPath),
            JsonOptions);

        var catalogEntries = catalog!;
        catalogEntries.Single(x => x.PackageVersion == "v1.0.0").Should()
            .Match<ApprovedProtocolPackageCatalogEntryRecord>(x =>
                x.ApprovalStatus == ProtocolPackageApprovalStatus.DraftPrivate &&
                !x.IsLatestForCompatibleProfiles);
        catalogEntries.Single(x => x.PackageVersion == "v1.1.1").ApprovalStatus.Should()
            .Be(ProtocolPackageApprovalStatus.DraftPrivate);
    }

    [Fact]
    public void Promote_WithoutVersion_WithCompleteSourcesAfterDraft_DerivesNextEvenMinorProductionVersion()
    {
        var paths = CreatePaths();
        WriteCompleteSourcePackage(paths.WorkingSourceRoot);
        var existingDraftEntry = CreateCatalogEntry(
            "v1.1.1",
            isLatestForCompatibleProfiles: false,
            approvalStatus: ProtocolPackageApprovalStatus.DraftPrivate);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.ServerCatalogPath)!);
        File.WriteAllText(paths.ServerCatalogPath, JsonSerializer.Serialize(new[] { existingDraftEntry }, JsonOptions));

        var result = new ProtocolPackagePromotionService().Promote(CreateAutoOptions(paths));

        result.ReleaseManifest.PackageVersion.Should().Be("v1.2.0");
        result.ReleaseManifest.ApprovalStatus.Should().Be(ProtocolPackageApprovalStatus.ApprovedInternal);
        result.CatalogEntry.IsApprovedForElectionOpen.Should().BeTrue();
        result.CatalogEntry.IsLatestForCompatibleProfiles.Should().BeTrue();
        result.IncompleteSourceFiles.Should().BeEmpty();
    }

    [Fact]
    public void Promote_WhenAccessLocationChanges_ChangesReleaseManifestHashOnly()
    {
        var paths = CreatePaths();
        WriteCompleteSourcePackage(paths.WorkingSourceRoot);
        var service = new ProtocolPackagePromotionService();

        var first = service.Promote(CreateOptions(paths));
        var second = service.Promote(CreateOptions(
            paths,
            publicBaseUrl: "https://docs.hushnetwork.social/protocol-omega/hushvoting-v1"));

        second.SpecificationManifest.PackageHash.Should().Be(first.SpecificationManifest.PackageHash);
        second.ProofManifest.PackageHash.Should().Be(first.ProofManifest.PackageHash);
        second.ReleaseManifest.ReleaseManifestHash.Should().NotBe(first.ReleaseManifest.ReleaseManifestHash);
        second.ReleaseManifest.SpecAccessLocations.Single().Location.Should()
            .StartWith("https://docs.hushnetwork.social/protocol-omega/hushvoting-v1/v1.0.0/");
    }

    [Fact]
    public void Promote_WithMissingRequiredFile_FailsClosedAndReportsMissingFile()
    {
        var paths = CreatePaths();
        WriteCompleteSourcePackage(
            paths.WorkingSourceRoot,
            skippedRelativePath:
                $"{ProtocolPackagePromotionService.SpecificationPackageFolderName}/Protocol-Omega-HushVoting-v1-Spec.md");
        var service = new ProtocolPackagePromotionService();

        var act = () => service.Promote(CreateOptions(paths));

        act.Should().Throw<ProtocolPackagePromotionException>()
            .Where(x => x.MissingSourceFiles.Contains(
                $"{ProtocolPackagePromotionService.SpecificationPackageFolderName}/Protocol-Omega-HushVoting-v1-Spec.md"));
        File.Exists(paths.ServerCatalogPath).Should().BeFalse();
        Directory.Exists(paths.OfficialArtifactsRoot).Should().BeFalse();
    }

    [Fact]
    public void Promote_WithUnsafePackageVersion_FailsBeforeWritingArtifacts()
    {
        var paths = CreatePaths();
        WriteCompleteSourcePackage(paths.WorkingSourceRoot);
        var service = new ProtocolPackagePromotionService();

        var act = () => service.Promote(CreateOptions(paths, packageVersion: "..\\v1.0.0"));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Package version must be a simple directory name*");
        Directory.Exists(paths.OfficialArtifactsRoot).Should().BeFalse();
        Directory.Exists(paths.WebsitePublicArtifactsRoot!).Should().BeFalse();
        Directory.Exists(paths.PublicPackageRepositoryArtifactsRoot!).Should().BeFalse();
        File.Exists(paths.ServerCatalogPath).Should().BeFalse();
    }

    [Fact]
    public void Promote_WithScaffoldEnabled_CreatesMissingSourceFilesAndPackageArtifacts()
    {
        var paths = CreatePaths();
        var service = new ProtocolPackagePromotionService();

        var result = service.Promote(CreateOptions(paths, scaffoldMissingSourceFiles: true));

        result.Succeeded.Should().BeTrue();
        result.MissingSourceFiles.Should().BeEmpty();
        result.SpecificationManifest.Files.Should().HaveCount(ProtocolPackagePromotionService.RequiredSpecificationFiles.Count);
        result.ProofManifest.Files.Should().HaveCount(ProtocolPackagePromotionService.RequiredProofFiles.Count);
        result.ReleaseManifest.ReleaseFiles.Should().HaveCount(ProtocolPackagePromotionService.RequiredReleaseFiles.Count);
        File.Exists(Path.Combine(
            paths.WorkingSourceRoot,
            "ChangeLog.md")).Should().BeTrue();
        File.ReadAllText(Path.Combine(
                paths.WorkingSourceRoot,
                "ChangeLog.md"))
            .Should().Contain("specified_not_implemented_yet");
        File.Exists(Path.Combine(
            paths.WorkingSourceRoot,
            ProtocolPackagePromotionService.SpecificationPackageFolderName,
            "README.md")).Should().BeTrue();
        File.ReadAllText(Path.Combine(
                paths.WorkingSourceRoot,
                ProtocolPackagePromotionService.SpecificationPackageFolderName,
                "README.md"))
            .Should().Contain("specified_not_implemented_yet");
        File.Exists(paths.ServerCatalogPath).Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private ProtocolPackagePromotionPaths CreatePaths() =>
        new(
            Path.Combine(_tempRoot, "working"),
            Path.Combine(_tempRoot, "official"),
            Path.Combine(_tempRoot, "server", "ApprovedProtocolPackageCatalog.json"),
            Path.Combine(_tempRoot, "website", "public", "protocol-omega", "hushvoting-v1"),
            Path.Combine(_tempRoot, "protocol-omega-packages", "hushvoting-v1"));

    private static ProtocolPackagePromotionOptions CreateOptions(
        ProtocolPackagePromotionPaths paths,
        bool scaffoldMissingSourceFiles = false,
        string publicBaseUrl = "https://www.hushnetwork.social/protocol-omega/hushvoting-v1",
        string packageVersion = "v1.0.0") =>
        ProtocolPackagePromotionOptions.Create(
            paths,
            packageVersion,
            scaffoldMissingSourceFiles,
            publicBaseUrl: publicBaseUrl,
            generatedAt: FixedGeneratedAt);

    private static ProtocolPackagePromotionOptions CreateAutoOptions(
        ProtocolPackagePromotionPaths paths,
        bool scaffoldMissingSourceFiles = false,
        string publicBaseUrl = "https://www.hushnetwork.social/protocol-omega/hushvoting-v1") =>
        ProtocolPackagePromotionOptions.Create(
            paths,
            scaffoldMissingSourceFiles: scaffoldMissingSourceFiles,
            publicBaseUrl: publicBaseUrl,
            generatedAt: FixedGeneratedAt);

    private static void WriteCompleteSourcePackage(
        string workingSourceRoot,
        string? skippedRelativePath = null)
    {
        WriteReleaseFiles(
            workingSourceRoot,
            skippedRelativePath,
            includeIncompleteMarker: false);
        WritePackageFiles(
            workingSourceRoot,
            ProtocolPackagePromotionService.SpecificationPackageFolderName,
            ProtocolPackagePromotionService.RequiredSpecificationFiles,
            skippedRelativePath,
            includeIncompleteMarker: false);
        WritePackageFiles(
            workingSourceRoot,
            ProtocolPackagePromotionService.ProofPackageFolderName,
            ProtocolPackagePromotionService.RequiredProofFiles,
            skippedRelativePath,
            includeIncompleteMarker: false);
    }

    private static void WriteIncompleteSourcePackage(string workingSourceRoot)
    {
        WriteReleaseFiles(
            workingSourceRoot,
            skippedRelativePath: null,
            includeIncompleteMarker: true);
        WritePackageFiles(
            workingSourceRoot,
            ProtocolPackagePromotionService.SpecificationPackageFolderName,
            ProtocolPackagePromotionService.RequiredSpecificationFiles,
            skippedRelativePath: null,
            includeIncompleteMarker: true);
        WritePackageFiles(
            workingSourceRoot,
            ProtocolPackagePromotionService.ProofPackageFolderName,
            ProtocolPackagePromotionService.RequiredProofFiles,
            skippedRelativePath: null,
            includeIncompleteMarker: true);
    }

    private static void WriteReleaseFiles(
        string workingSourceRoot,
        string? skippedRelativePath,
        bool includeIncompleteMarker)
    {
        foreach (var relativePath in ProtocolPackagePromotionService.RequiredReleaseFiles)
        {
            var normalizedRelativePath = relativePath.Replace('\\', '/');
            if (string.Equals(normalizedRelativePath, skippedRelativePath, StringComparison.Ordinal))
            {
                continue;
            }

            var fullPath = Path.Combine(workingSourceRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var content = includeIncompleteMarker
                ? $"# {relativePath}{Environment.NewLine}{Environment.NewLine}Status: specified_not_implemented_yet{Environment.NewLine}Package: {ProtocolPackagePromotionService.ReleasePackageFolderName}{Environment.NewLine}Version: v1.0.0"
                : $"# {relativePath}{Environment.NewLine}{Environment.NewLine}Package: {ProtocolPackagePromotionService.ReleasePackageFolderName}{Environment.NewLine}Version: v1.0.0";
            File.WriteAllText(fullPath, content);
        }
    }

    private static void WritePackageFiles(
        string workingSourceRoot,
        string folderName,
        IReadOnlyList<string> requiredFiles,
        string? skippedRelativePath,
        bool includeIncompleteMarker)
    {
        foreach (var relativePath in requiredFiles)
        {
            var combinedRelativePath = $"{folderName}/{relativePath.Replace('\\', '/')}";
            if (string.Equals(combinedRelativePath, skippedRelativePath, StringComparison.Ordinal))
            {
                continue;
            }

            var fullPath = Path.Combine(workingSourceRoot, folderName, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var content = relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? includeIncompleteMarker
                    ? $$"""{ "status": "specified_not_implemented_yet", "packageVersion": "v1.0.0", "file": "{{relativePath.Replace('\\', '/')}}", "folder": "{{folderName}}" }"""
                    : $$"""{ "file": "{{relativePath.Replace('\\', '/')}}", "folder": "{{folderName}}" }"""
                : includeIncompleteMarker
                    ? $"# {relativePath}{Environment.NewLine}{Environment.NewLine}Status: specified_not_implemented_yet{Environment.NewLine}Package: {folderName}{Environment.NewLine}Version: v1.0.0"
                    : $"# {relativePath}{Environment.NewLine}{Environment.NewLine}Package: {folderName}";
            File.WriteAllText(fullPath, content);
        }
    }

    private static ApprovedProtocolPackageCatalogEntryRecord CreateCatalogEntry(
        string packageVersion,
        bool isLatestForCompatibleProfiles,
        ProtocolPackageApprovalStatus approvalStatus = ProtocolPackageApprovalStatus.ApprovedInternal) =>
        ElectionModelFactory.CreateApprovedProtocolPackageCatalogEntry(
            packageId: "omega-hushvoting-v1",
            packageVersion: packageVersion,
            specPackageHash: Hash('a'),
            proofPackageHash: Hash('b'),
            releaseManifestHash: Hash('c'),
            compatibleProfileIds:
            [
                ElectionSelectableProfileCatalog.AdminOnlyProductionProfileId,
            ],
            approvalStatus,
            isLatestForCompatibleProfiles,
            specAccessLocations:
            [
                CreateAccessLocation(Hash('d')),
            ],
            proofAccessLocations:
            [
                CreateAccessLocation(Hash('e')),
            ],
            approvedAt: FixedGeneratedAt);

    private static ProtocolPackageAccessLocationRecord CreateAccessLocation(string contentHash) =>
        ElectionModelFactory.CreateProtocolPackageAccessLocation(
            ProtocolPackageAccessLocationKind.PublicWebsite,
            "Website",
            "https://www.hushnetwork.social/protocol-omega/hushvoting-v1",
            contentHash);

    private static string Hash(char value) =>
        new(char.ToLowerInvariant(value), 64);
}
