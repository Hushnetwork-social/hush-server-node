using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using FluentAssertions;
using HushNode.Elections;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ProtocolPackageCatalogRemoteSyncServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    [Fact]
    public async Task SyncLatestApprovedPackageForProfileAsync_DownloadsOfficialAssetAndImportsCatalogEntry()
    {
        var oldEntry = CreateCatalogEntry("v1.2.0", isLatest: true, hashSeed: 'a');
        var newPackage = CreateReleaseAsset("v1.2.1", "dkg-prod-3of5");
        var releasesJson = $$"""
            [
              {
                "tag_name": "ProtocolOmega-HushVoting-v1-v1.2.1",
                "assets": [
                  {
                    "name": "Protocol-Omega-HushVoting-v1-Artifacts-v1.2.1.zip",
                    "browser_download_url": "https://downloads.test/protocol-omega-v1.2.1.zip",
                    "digest": "sha256:{{Sha256(newPackage.AssetBytes)}}"
                  }
                ]
              }
            ]
            """;

        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
            request.RequestUri?.AbsoluteUri switch
            {
                "https://api.test/releases" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(releasesJson, Encoding.UTF8, "application/json"),
                },
                "https://downloads.test/protocol-omega-v1.2.1.zip" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(newPackage.AssetBytes),
                },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            }));
        var existingEntries = new List<ApprovedProtocolPackageCatalogEntryRecord>
        {
            oldEntry,
        };
        var service = new ProtocolPackageCatalogRemoteSyncService(
            CreateUnitOfWorkProvider(existingEntries, out var unitOfWork),
            httpClient,
            ProtocolPackageCatalogRemoteSyncOptions.Default with
            {
                ReleasesApiUrl = "https://api.test/releases",
                RequestTimeoutSeconds = 5,
            },
            NullLogger<ProtocolPackageCatalogRemoteSyncService>.Instance);

        var result = await service.SyncLatestApprovedPackageForProfileAsync("dkg-prod-3of5");

        result.IsSuccess.Should().BeTrue();
        result.PackageVersion.Should().Be("v1.2.1");
        existingEntries.Should().HaveCount(2);
        existingEntries.Single(x => x.PackageVersion == "v1.2.0").IsLatestForCompatibleProfiles.Should().BeFalse();
        existingEntries.Single(x => x.PackageVersion == "v1.2.1").Should().Match<ApprovedProtocolPackageCatalogEntryRecord>(x =>
            x.IsLatestForCompatibleProfiles &&
            x.SpecPackageHash == newPackage.SpecPackageHash &&
            x.ProofPackageHash == newPackage.ProofPackageHash &&
            x.ReleaseManifestHash == newPackage.ReleaseManifestHash);
        unitOfWork.Verify(x => x.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task SyncLatestApprovedPackageForProfileAsync_ToleratesBomPrefixedReleaseManifest()
    {
        var newPackage = CreateReleaseAsset(
            "v1.2.1",
            "dkg-prod-3of5",
            includeManifestBom: true);
        var releasesJson = $$"""
            [
              {
                "tag_name": "ProtocolOmega-HushVoting-v1-v1.2.1",
                "assets": [
                  {
                    "name": "Protocol-Omega-HushVoting-v1-Artifacts-v1.2.1.zip",
                    "browser_download_url": "https://downloads.test/protocol-omega-v1.2.1.zip",
                    "digest": "sha256:{{Sha256(newPackage.AssetBytes)}}"
                  }
                ]
              }
            ]
            """;

        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
            request.RequestUri?.AbsoluteUri switch
            {
                "https://api.test/releases" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(releasesJson, Encoding.UTF8, "application/json"),
                },
                "https://downloads.test/protocol-omega-v1.2.1.zip" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(newPackage.AssetBytes),
                },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            }));
        var existingEntries = new List<ApprovedProtocolPackageCatalogEntryRecord>();
        var service = new ProtocolPackageCatalogRemoteSyncService(
            CreateUnitOfWorkProvider(existingEntries, out var unitOfWork),
            httpClient,
            ProtocolPackageCatalogRemoteSyncOptions.Default with
            {
                ReleasesApiUrl = "https://api.test/releases",
                RequestTimeoutSeconds = 5,
            },
            NullLogger<ProtocolPackageCatalogRemoteSyncService>.Instance);

        var result = await service.SyncLatestApprovedPackageForProfileAsync("dkg-prod-3of5");

        result.IsSuccess.Should().BeTrue();
        result.PackageVersion.Should().Be("v1.2.1");
        existingEntries.Should().ContainSingle(x => x.PackageVersion == "v1.2.1");
        unitOfWork.Verify(x => x.CommitAsync(), Times.Once);
    }

    private static IUnitOfWorkProvider<ElectionsDbContext> CreateUnitOfWorkProvider(
        List<ApprovedProtocolPackageCatalogEntryRecord> existingEntries,
        out Mock<IWritableUnitOfWork<ElectionsDbContext>> unitOfWork)
    {
        var repository = new Mock<IElectionsRepository>();
        repository
            .Setup(x => x.GetApprovedProtocolPackageCatalogEntriesAsync())
            .ReturnsAsync(existingEntries);
        repository
            .Setup(x => x.SaveApprovedProtocolPackageCatalogEntryAsync(It.IsAny<ApprovedProtocolPackageCatalogEntryRecord>()))
            .Callback<ApprovedProtocolPackageCatalogEntryRecord>(existingEntries.Add)
            .Returns(Task.CompletedTask);
        repository
            .Setup(x => x.UpdateApprovedProtocolPackageCatalogEntryAsync(It.IsAny<ApprovedProtocolPackageCatalogEntryRecord>()))
            .Callback<ApprovedProtocolPackageCatalogEntryRecord>(updatedEntry =>
            {
                var index = existingEntries.FindIndex(x =>
                    string.Equals(x.PackageId, updatedEntry.PackageId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.PackageVersion, updatedEntry.PackageVersion, StringComparison.OrdinalIgnoreCase));

                if (index >= 0)
                {
                    existingEntries[index] = updatedEntry;
                }
            })
            .Returns(Task.CompletedTask);

        unitOfWork = new Mock<IWritableUnitOfWork<ElectionsDbContext>>();
        unitOfWork
            .Setup(x => x.GetRepository<IElectionsRepository>())
            .Returns(repository.Object);
        unitOfWork
            .Setup(x => x.CommitAsync())
            .Returns(Task.CompletedTask);

        var unitOfWorkProvider = new Mock<IUnitOfWorkProvider<ElectionsDbContext>>();
        unitOfWorkProvider
            .Setup(x => x.CreateWritable())
            .Returns(unitOfWork.Object);

        return unitOfWorkProvider.Object;
    }

    private static ReleaseAssetFixture CreateReleaseAsset(
        string packageVersion,
        string compatibleProfileId,
        bool includeManifestBom = false)
    {
        var specPackageBytes = CreateZip(("spec.md", Encoding.UTF8.GetBytes("spec")));
        var proofPackageBytes = CreateZip(("proof.md", Encoding.UTF8.GetBytes("proof")));
        var specPackageHash = Sha256(specPackageBytes);
        var proofPackageHash = Sha256(proofPackageBytes);
        var releaseManifestHash = Hash('c');
        var changeLogBytes = Encoding.UTF8.GetBytes("release notes");
        var manifest = ElectionModelFactory.CreateProtocolOmegaPackageReleaseManifest(
            "omega-hushvoting-v1",
            packageVersion,
            specPackageHash,
            proofPackageHash,
            releaseManifestHash,
            ProtocolPackageApprovalStatus.ApprovedInternal,
            [compatibleProfileId],
            [CreateAccessLocation(specPackageHash)],
            [CreateAccessLocation(proofPackageHash)],
            releaseFiles:
            [
                ElectionModelFactory.CreateProtocolPackageFileHash(
                    "ChangeLog.md",
                    Sha256(changeLogBytes),
                    changeLogBytes.Length,
                    "text/markdown"),
            ],
            releasedAt: new DateTime(2026, 6, 4, 16, 0, 0, DateTimeKind.Utc));

        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        if (includeManifestBom)
        {
            manifestBytes = [0xEF, 0xBB, 0xBF, .. manifestBytes];
        }

        var assetBytes = CreateZip(
            ($"{packageVersion}/ProtocolOmegaPackageManifest.json", manifestBytes),
            ($"{packageVersion}/ChangeLog.md", changeLogBytes),
            ($"{packageVersion}/Protocol-Specification-Package/Protocol-Specification-Package.zip", specPackageBytes),
            ($"{packageVersion}/Protocol-Proof-And-Crypto-Review/Protocol-Proof-And-Crypto-Review.zip", proofPackageBytes));

        return new ReleaseAssetFixture(assetBytes, specPackageHash, proofPackageHash, releaseManifestHash);
    }

    private static byte[] CreateZip(params (string Path, byte[] Bytes)[] entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, bytes) in entries)
            {
                var entry = zip.CreateEntry(path);
                using var stream = entry.Open();
                stream.Write(bytes);
            }
        }

        return memory.ToArray();
    }

    private static ApprovedProtocolPackageCatalogEntryRecord CreateCatalogEntry(
        string packageVersion,
        bool isLatest,
        char hashSeed) =>
        ElectionModelFactory.CreateApprovedProtocolPackageCatalogEntry(
            packageId: "omega-hushvoting-v1",
            packageVersion: packageVersion,
            specPackageHash: Hash(hashSeed),
            proofPackageHash: Hash((char)(hashSeed + 1)),
            releaseManifestHash: Hash((char)(hashSeed + 2)),
            compatibleProfileIds:
            [
                "dkg-prod-3of5",
            ],
            approvalStatus: ProtocolPackageApprovalStatus.ApprovedInternal,
            isLatestForCompatibleProfiles: isLatest,
            specAccessLocations:
            [
                CreateAccessLocation(Hash((char)(hashSeed + 3))),
            ],
            proofAccessLocations:
            [
                CreateAccessLocation(Hash((char)(hashSeed + 4))),
            ],
            approvedAt: new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc));

    private static ProtocolPackageAccessLocationRecord CreateAccessLocation(string contentHash) =>
        ElectionModelFactory.CreateProtocolPackageAccessLocation(
            ProtocolPackageAccessLocationKind.PublicWebsite,
            "Test package",
            $"https://tests.hushnetwork.local/{contentHash}.zip",
            contentHash);

    private static string Hash(char seed) =>
        new(seed, 64);

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record ReleaseAssetFixture(
        byte[] AssetBytes,
        string SpecPackageHash,
        string ProofPackageHash,
        string ReleaseManifestHash);

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
