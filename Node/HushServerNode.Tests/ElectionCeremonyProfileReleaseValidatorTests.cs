using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using HushNode.Elections;
using HushServerNode.Testing;
using Xunit;

namespace HushServerNode.Tests;

public sealed class ElectionCeremonyProfileReleaseValidatorTests
{
    [Fact]
    public void ValidateFromWorkspaceRoot_WhenManifestMissing_ShouldReportMissing()
    {
        using var temp = new TemporaryWorkspaceRoot();

        var result = ElectionCeremonyProfileReleaseValidator.ValidateFromWorkspaceRoot(temp.RootPath);

        result.IsValid.Should().BeFalse();
        result.Notes.Should().Be("Approved ceremony profile release manifest missing");
    }

    [Fact]
    public void ValidateFromWorkspaceRoot_WhenCatalogMatchesManifest_ShouldReportValid()
    {
        using var temp = new TemporaryWorkspaceRoot();
        temp.CreateCatalogAndReleaseManifest();

        var result = ElectionCeremonyProfileReleaseValidator.ValidateFromWorkspaceRoot(temp.RootPath);

        result.IsValid.Should().BeTrue();
        result.Notes.Should().Be("Approved ceremony profile release manifest and installed files match");
    }

    [Fact]
    public void ValidateFromWorkspaceRoot_WhenCatalogChecksumDiffers_ShouldReportMismatch()
    {
        using var temp = new TemporaryWorkspaceRoot();
        temp.CreateCatalogAndReleaseManifest();
        File.AppendAllText(temp.CatalogPath, "tampered");

        var result = ElectionCeremonyProfileReleaseValidator.ValidateFromWorkspaceRoot(temp.RootPath);

        result.IsValid.Should().BeFalse();
        result.Notes.Should().Be("SHA-256 mismatch for 'hush-server-node/Node/HushServerNode/ceremony-profiles/omega-v1.0.0/approved-ceremony-profiles.json'");
    }

    private sealed class TemporaryWorkspaceRoot : IDisposable
    {
        public TemporaryWorkspaceRoot()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"feat097-ceremony-profiles-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string CatalogPath =>
            Path.Combine(
                RootPath,
                "hush-server-node",
                "Node",
                "HushServerNode",
                "ceremony-profiles",
                "omega-v1.0.0",
                "approved-ceremony-profiles.json");

        public void CreateCatalogAndReleaseManifest()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath)!);

            var catalog = new ElectionCeremonyProfileCatalogManifest(
                Version: ElectionCeremonyProfileCatalog.ExpectedVersion,
                GeneratedBy: "unit-test",
                Profiles:
                [
                    new ElectionCeremonyProfileCatalogEntry(
                        ElectionCeremonyProfileCatalog.DevProfileId,
                        "Development 3 of 5",
                        "Dev profile",
                        "hush-dkg-profile-manifest",
                        "omega-v1.0.0-dev-3of5",
                        ElectionCeremonyProfileCatalog.InitialTrusteeCount,
                        ElectionCeremonyProfileCatalog.InitialRequiredApprovalCount,
                        DevOnly: true),
                    new ElectionCeremonyProfileCatalogEntry(
                        ElectionCeremonyProfileCatalog.ProductionProfileId,
                        "Production-Like 3 of 5",
                        "Production-like profile",
                        "hush-dkg-profile-manifest",
                        "omega-v1.0.0-prod-3of5",
                        ElectionCeremonyProfileCatalog.InitialTrusteeCount,
                        ElectionCeremonyProfileCatalog.InitialRequiredApprovalCount,
                        DevOnly: false),
                ]);

            File.WriteAllText(
                CatalogPath,
                JsonSerializer.Serialize(catalog, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

            var releaseDirectory = Path.Combine(
                RootPath,
                "hush-memory-bank",
                "Features",
                "03_IN_PROGRESS",
                "FEAT-097-election-key-ceremony-share-lifecycle");
            Directory.CreateDirectory(releaseDirectory);

            var releaseManifest = new ElectionCeremonyProfileReleaseManifest(
                Version: ElectionCeremonyProfileCatalog.ExpectedVersion,
                Provenance: "unit-test",
                GeneratedBy: "unit-test",
                Files:
                [
                    new ElectionCeremonyProfileReleaseFile(
                        "hush-server-node/Node/HushServerNode/ceremony-profiles/omega-v1.0.0/approved-ceremony-profiles.json",
                        ComputeSha256Hex(CatalogPath)),
                ]);

            File.WriteAllText(
                Path.Combine(releaseDirectory, "approved-ceremony-profile-release.json"),
                JsonSerializer.Serialize(releaseManifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private static string ComputeSha256Hex(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash);
        }
    }
}
