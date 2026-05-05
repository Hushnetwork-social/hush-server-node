using FluentAssertions;
using HushShared.Elections.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ProtocolPackageRecordTests
{
    [Fact]
    public void CreateProtocolPackageManifest_WithRequiredFields_NormalizesHashesAndProfiles()
    {
        var manifest = ElectionModelFactory.CreateProtocolPackageManifest(
            packageId: " omega-hushvoting-v1-spec ",
            packageVersion: " v1.0.0 ",
            packageKind: ProtocolPackageKind.Specification,
            packageStatus: ProtocolPackageApprovalStatus.ApprovedInternal,
            packageHash: $"SHA256:{Hash('A')}",
            archiveFileName: "Protocol-Specification-Package.zip",
            schemaVersion: "1.0",
            compatibleProfileIds:
            [
                "organizational_remote_voting_trustee_threshold_v1",
                "organizational_remote_voting_trustee_threshold_v1",
            ],
            files:
            [
                ElectionModelFactory.CreateProtocolPackageFileHash(
                    "README.md",
                    Hash('b'),
                    sizeBytes: 128,
                    mediaType: "text/markdown"),
            ],
            accessLocations:
            [
                CreateAccessLocation(Hash('c')),
            ]);

        manifest.PackageId.Should().Be("omega-hushvoting-v1-spec");
        manifest.PackageVersion.Should().Be("v1.0.0");
        manifest.PackageHash.Should().Be(Hash('a'));
        manifest.CompatibleProfileIds.Should().Equal("organizational_remote_voting_trustee_threshold_v1");
        manifest.Files[0].Sha256Hash.Should().Be(Hash('b'));
        manifest.AccessLocations[0].ContentHash.Should().Be(Hash('c'));
    }

    [Fact]
    public void CreateProtocolPackageManifest_WithoutCompatibleProfile_ShouldThrow()
    {
        var act = () => ElectionModelFactory.CreateProtocolPackageManifest(
            packageId: "omega-hushvoting-v1-spec",
            packageVersion: "v1.0.0",
            packageKind: ProtocolPackageKind.Specification,
            packageStatus: ProtocolPackageApprovalStatus.ApprovedInternal,
            packageHash: Hash('a'),
            archiveFileName: "Protocol-Specification-Package.zip",
            schemaVersion: "1.0",
            compatibleProfileIds: Array.Empty<string>(),
            files:
            [
                ElectionModelFactory.CreateProtocolPackageFileHash("README.md", Hash('b'), 128),
            ],
            accessLocations:
            [
                CreateAccessLocation(Hash('c')),
            ]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*At least one value*")
            .And.ParamName.Should().Be("CompatibleProfileIds");
    }

    [Fact]
    public void CreateProtocolPackageManifest_WithInvalidHash_ShouldThrow()
    {
        var act = () => ElectionModelFactory.CreateProtocolPackageFileHash(
            "README.md",
            "not-a-sha256",
            sizeBytes: 128);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*SHA-256*")
            .And.ParamName.Should().Be("Sha256Hash");
    }

    [Fact]
    public void CreateApprovedProtocolPackageCatalogEntry_RequiresAccessLocations()
    {
        var act = () => ElectionModelFactory.CreateApprovedProtocolPackageCatalogEntry(
            packageId: "omega-hushvoting-v1",
            packageVersion: "v1.0.0",
            specPackageHash: Hash('a'),
            proofPackageHash: Hash('b'),
            releaseManifestHash: Hash('c'),
            compatibleProfileIds:
            [
                "organizational_remote_voting_admin_only_v1",
            ],
            approvalStatus: ProtocolPackageApprovalStatus.ApprovedInternal,
            isLatestForCompatibleProfiles: true,
            specAccessLocations: Array.Empty<ProtocolPackageAccessLocationRecord>(),
            proofAccessLocations:
            [
                CreateAccessLocation(Hash('d')),
            ]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*At least one value*")
            .And.ParamName.Should().Be("SpecAccessLocations");
    }

    [Fact]
    public void CreateProtocolPackageBindingFromCatalog_CapturesCurrentCatalogRefs()
    {
        var catalogEntry = CreateCatalogEntry();

        var binding = ElectionModelFactory.CreateProtocolPackageBindingFromCatalog(
            ElectionId.NewElectionId,
            catalogEntry,
            selectedProfileId: "organizational_remote_voting_trustee_threshold_v1",
            draftRevision: 3,
            boundByPublicAddress: "owner-address");

        binding.PackageId.Should().Be(catalogEntry.PackageId);
        binding.PackageVersion.Should().Be(catalogEntry.PackageVersion);
        binding.SpecPackageHash.Should().Be(catalogEntry.SpecPackageHash);
        binding.ProofPackageHash.Should().Be(catalogEntry.ProofPackageHash);
        binding.ReleaseManifestHash.Should().Be(catalogEntry.ReleaseManifestHash);
        binding.Status.Should().Be(ProtocolPackageBindingStatus.Latest);
        binding.Source.Should().Be(ProtocolPackageBindingSource.CatalogSelection);
        binding.BlocksElectionOpen.Should().BeFalse();
        binding.DraftRevision.Should().Be(3);
    }

    [Fact]
    public void CreateProtocolPackageBindingFromCatalog_WithIncompatibleProfile_ShouldThrow()
    {
        var catalogEntry = CreateCatalogEntry();

        var act = () => ElectionModelFactory.CreateProtocolPackageBindingFromCatalog(
            ElectionId.NewElectionId,
            catalogEntry,
            selectedProfileId: "unsupported_profile_v1",
            draftRevision: 1,
            boundByPublicAddress: "owner-address");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*not compatible*");
    }

    [Fact]
    public void ProtocolPackageBinding_SealAtOpen_MakesRefsImmutable()
    {
        var catalogEntry = CreateCatalogEntry();
        var binding = ElectionModelFactory.CreateProtocolPackageBindingFromCatalog(
            ElectionId.NewElectionId,
            catalogEntry,
            selectedProfileId: "organizational_remote_voting_trustee_threshold_v1",
            draftRevision: 1,
            boundByPublicAddress: "owner-address");

        var sealedBinding = binding.SealAtOpen(
            DateTime.UtcNow,
            sealedByPublicAddress: "owner-address");

        sealedBinding.Status.Should().Be(ProtocolPackageBindingStatus.Sealed);
        sealedBinding.Source.Should().Be(ProtocolPackageBindingSource.SealedAtOpen);
        sealedBinding.SealedAt.Should().NotBeNull();
        sealedBinding.BlocksElectionOpen.Should().BeFalse();

        var act = () => sealedBinding.RefreshFromCatalog(
            catalogEntry,
            selectedProfileId: "organizational_remote_voting_trustee_threshold_v1",
            draftRevision: 2,
            refreshedByPublicAddress: "owner-address",
            refreshedAt: DateTime.UtcNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*immutable*");
    }

    [Fact]
    public void CreateMigrationBackfillProtocolPackageBinding_IsReferenceOnlyAndBlocksOpen()
    {
        var binding = ElectionModelFactory.CreateMigrationBackfillProtocolPackageBinding(
            ElectionId.NewElectionId,
            CreateCatalogEntry(),
            selectedProfileId: "organizational_remote_voting_trustee_threshold_v1",
            draftRevision: 1,
            backfilledByPublicAddress: "system-migration");

        binding.Status.Should().Be(ProtocolPackageBindingStatus.ReferenceOnly);
        binding.Source.Should().Be(ProtocolPackageBindingSource.MigrationBackfill);
        binding.BlocksElectionOpen.Should().BeTrue();
        binding.SealedAt.Should().BeNull();
    }

    [Fact]
    public void ProtocolPackageBinding_WithSealedAtOpenSourceWithoutSealedStatus_ShouldThrow()
    {
        var act = () => new ProtocolPackageBindingRecord(
            Guid.NewGuid(),
            ElectionId.NewElectionId,
            PackageId: "omega-hushvoting-v1",
            PackageVersion: "v1.0.0",
            SelectedProfileId: "organizational_remote_voting_trustee_threshold_v1",
            SpecPackageHash: Hash('a'),
            ProofPackageHash: Hash('b'),
            ReleaseManifestHash: Hash('c'),
            SpecAccessLocations:
            [
                CreateAccessLocation(Hash('d')),
            ],
            ProofAccessLocations:
            [
                CreateAccessLocation(Hash('e')),
            ],
            PackageApprovalStatus: ProtocolPackageApprovalStatus.ApprovedInternal,
            Status: ProtocolPackageBindingStatus.Latest,
            Source: ProtocolPackageBindingSource.SealedAtOpen,
            DraftRevision: 1,
            BoundAt: DateTime.UtcNow,
            SealedAt: null,
            BoundByPublicAddress: "owner-address",
            ExternalReviewStatus: ProtocolPackageExternalReviewStatus.NotReviewed,
            SourceTransactionId: null,
            SourceBlockHeight: null,
            SourceBlockId: null);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*sealed-at-open*")
            .And.ParamName.Should().Be("Source");
    }

    private static ApprovedProtocolPackageCatalogEntryRecord CreateCatalogEntry() =>
        ElectionModelFactory.CreateApprovedProtocolPackageCatalogEntry(
            packageId: "omega-hushvoting-v1",
            packageVersion: "v1.0.0",
            specPackageHash: Hash('a'),
            proofPackageHash: Hash('b'),
            releaseManifestHash: Hash('c'),
            compatibleProfileIds:
            [
                "organizational_remote_voting_trustee_threshold_v1",
            ],
            approvalStatus: ProtocolPackageApprovalStatus.ApprovedInternal,
            isLatestForCompatibleProfiles: true,
            specAccessLocations:
            [
                CreateAccessLocation(Hash('d')),
            ],
            proofAccessLocations:
            [
                CreateAccessLocation(Hash('e')),
            ]);

    private static ProtocolPackageAccessLocationRecord CreateAccessLocation(string contentHash) =>
        ElectionModelFactory.CreateProtocolPackageAccessLocation(
            ProtocolPackageAccessLocationKind.PublicWebsite,
            "Website",
            "https://www.hushnetwork.social/protocol-omega/hushvoting-v1",
            contentHash);

    private static string Hash(char value) =>
        new(char.ToLowerInvariant(value), 64);
}
