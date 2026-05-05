using System.Text.Json;
using FluentAssertions;
using HushNode.Elections;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ProtocolPackageCatalogBootstrapperTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        $"hush-protocol-catalog-bootstrapper-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Startup_WithCatalogFile_UpsertsEntriesAndDemotesPriorLatestEntry()
    {
        Directory.CreateDirectory(_tempRoot);
        var catalogPath = Path.Combine(_tempRoot, "ApprovedProtocolPackageCatalog.json");
        var existingEntries = new List<ApprovedProtocolPackageCatalogEntryRecord>
        {
            CreateCatalogEntry("v1.0.0", isLatest: true, hashSeed: 'a'),
        };
        var shippedEntry = CreateCatalogEntry("v1.2.0", isLatest: true, hashSeed: 'f');
        File.WriteAllText(
            catalogPath,
            JsonSerializer.Serialize(
                new[] { shippedEntry },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true,
                }));

        var bootstrapper = CreateBootstrapper(catalogPath, existingEntries, out var unitOfWork);

        await bootstrapper.Startup();

        existingEntries.Should().HaveCount(2);
        existingEntries.Single(x => x.PackageVersion == "v1.0.0").IsLatestForCompatibleProfiles.Should().BeFalse();
        existingEntries.Single(x => x.PackageVersion == "v1.2.0").Should().BeEquivalentTo(shippedEntry);
        unitOfWork.Verify(x => x.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task Startup_WithMissingOptionalCatalog_DoesNotCreateUnitOfWork()
    {
        var missingCatalogPath = Path.Combine(_tempRoot, "missing.json");
        var unitOfWorkProvider = new Mock<IUnitOfWorkProvider<ElectionsDbContext>>();
        var bootstrapper = new ProtocolPackageCatalogBootstrapper(
            unitOfWorkProvider.Object,
            new ProtocolPackageCatalogOptions(missingCatalogPath, FailOnMissingCatalog: false),
            NullLogger<ProtocolPackageCatalogBootstrapper>.Instance);

        await bootstrapper.Startup();

        unitOfWorkProvider.Verify(x => x.CreateWritable(), Times.Never);
    }

    private static ProtocolPackageCatalogBootstrapper CreateBootstrapper(
        string catalogPath,
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

        return new ProtocolPackageCatalogBootstrapper(
            unitOfWorkProvider.Object,
            new ProtocolPackageCatalogOptions(catalogPath, FailOnMissingCatalog: false),
            NullLogger<ProtocolPackageCatalogBootstrapper>.Instance);
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

    private static string Hash(char seed)
    {
        const string hexCharacters = "0123456789abcdef";
        return new string(hexCharacters[seed % hexCharacters.Length], 64);
    }
}
