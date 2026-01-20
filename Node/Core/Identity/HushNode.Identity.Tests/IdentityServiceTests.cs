using FluentAssertions;
using HushNode.Caching;
using HushNode.Identity.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Identity.Model;
using Moq;
using Xunit;

namespace HushNode.Identity.Tests;

public class IdentityServiceTests
{
    private const string TestAddress = "test-public-signing-address";

    private static (IdentityService Sut, Mock<IIdentityStorageService> StorageMock, Mock<IIdentityCacheService> CacheMock) CreateService()
    {
        var storageMock = new Mock<IIdentityStorageService>();
        var cacheMock = new Mock<IIdentityCacheService>();

        var sut = new IdentityService(storageMock.Object, cacheMock.Object);

        return (sut, storageMock, cacheMock);
    }

    private static Profile CreateTestProfile(string address = TestAddress) => new(
        Alias: "Test User",
        ShortAlias: "TU",
        PublicSigningAddress: address,
        PublicEncryptAddress: "test-encrypt-address",
        IsPublic: true,
        BlockIndex: new BlockIndex(100));

    // =============================================================================
    // Cache Hit Tests
    // =============================================================================

    [Fact]
    public async Task RetrieveIdentityAsync_CacheHit_ReturnsCachedProfile()
    {
        // Arrange
        var (sut, storageMock, cacheMock) = CreateService();
        var cachedProfile = CreateTestProfile();
        cacheMock.Setup(x => x.GetIdentityAsync(TestAddress)).ReturnsAsync(cachedProfile);

        // Act
        var result = await sut.RetrieveIdentityAsync(TestAddress);

        // Assert
        result.Should().Be(cachedProfile);
    }

    [Fact]
    public async Task RetrieveIdentityAsync_CacheHit_DoesNotQueryStorage()
    {
        // Arrange
        var (sut, storageMock, cacheMock) = CreateService();
        var cachedProfile = CreateTestProfile();
        cacheMock.Setup(x => x.GetIdentityAsync(TestAddress)).ReturnsAsync(cachedProfile);

        // Act
        await sut.RetrieveIdentityAsync(TestAddress);

        // Assert
        storageMock.Verify(x => x.RetrieveIdentityAsync(It.IsAny<string>()), Times.Never);
    }

    // =============================================================================
    // Cache Miss with Existing Profile Tests
    // =============================================================================

    [Fact]
    public async Task RetrieveIdentityAsync_CacheMiss_QueriesStorage()
    {
        // Arrange
        var (sut, storageMock, cacheMock) = CreateService();
        var profile = CreateTestProfile();
        cacheMock.Setup(x => x.GetIdentityAsync(TestAddress)).ReturnsAsync((Profile?)null);
        storageMock.Setup(x => x.RetrieveIdentityAsync(TestAddress)).ReturnsAsync(profile);

        // Act
        await sut.RetrieveIdentityAsync(TestAddress);

        // Assert
        storageMock.Verify(x => x.RetrieveIdentityAsync(TestAddress), Times.Once);
    }

    [Fact]
    public async Task RetrieveIdentityAsync_CacheMiss_ExistingProfile_CachesResult()
    {
        // Arrange
        var (sut, storageMock, cacheMock) = CreateService();
        var profile = CreateTestProfile();
        cacheMock.Setup(x => x.GetIdentityAsync(TestAddress)).ReturnsAsync((Profile?)null);
        storageMock.Setup(x => x.RetrieveIdentityAsync(TestAddress)).ReturnsAsync(profile);

        // Act
        await sut.RetrieveIdentityAsync(TestAddress);

        // Assert
        cacheMock.Verify(x => x.SetIdentityAsync(TestAddress, profile), Times.Once);
    }

    [Fact]
    public async Task RetrieveIdentityAsync_CacheMiss_ExistingProfile_ReturnsProfile()
    {
        // Arrange
        var (sut, storageMock, cacheMock) = CreateService();
        var profile = CreateTestProfile();
        cacheMock.Setup(x => x.GetIdentityAsync(TestAddress)).ReturnsAsync((Profile?)null);
        storageMock.Setup(x => x.RetrieveIdentityAsync(TestAddress)).ReturnsAsync(profile);

        // Act
        var result = await sut.RetrieveIdentityAsync(TestAddress);

        // Assert
        result.Should().Be(profile);
    }

    // =============================================================================
    // Cache Miss with Non-Existing Profile Tests
    // =============================================================================

    [Fact]
    public async Task RetrieveIdentityAsync_CacheMiss_NonExistingProfile_DoesNotCache()
    {
        // Arrange
        var (sut, storageMock, cacheMock) = CreateService();
        var nonExistingProfile = new NonExistingProfile();
        cacheMock.Setup(x => x.GetIdentityAsync(TestAddress)).ReturnsAsync((Profile?)null);
        storageMock.Setup(x => x.RetrieveIdentityAsync(TestAddress)).ReturnsAsync(nonExistingProfile);

        // Act
        await sut.RetrieveIdentityAsync(TestAddress);

        // Assert
        cacheMock.Verify(x => x.SetIdentityAsync(It.IsAny<string>(), It.IsAny<Profile>()), Times.Never);
    }

    [Fact]
    public async Task RetrieveIdentityAsync_CacheMiss_NonExistingProfile_ReturnsNonExistingProfile()
    {
        // Arrange
        var (sut, storageMock, cacheMock) = CreateService();
        var nonExistingProfile = new NonExistingProfile();
        cacheMock.Setup(x => x.GetIdentityAsync(TestAddress)).ReturnsAsync((Profile?)null);
        storageMock.Setup(x => x.RetrieveIdentityAsync(TestAddress)).ReturnsAsync(nonExistingProfile);

        // Act
        var result = await sut.RetrieveIdentityAsync(TestAddress);

        // Assert
        result.Should().BeOfType<NonExistingProfile>();
    }
}
