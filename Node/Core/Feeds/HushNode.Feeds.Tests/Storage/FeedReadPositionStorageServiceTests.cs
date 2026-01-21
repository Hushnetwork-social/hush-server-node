using FluentAssertions;
using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.AutoMock;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushNode.Feeds.Tests.Storage;

/// <summary>
/// Tests for FeedReadPositionStorageService - orchestrates cache and repository.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class FeedReadPositionStorageServiceTests
{
    private const string TestUserId = "0x1234567890abcdef";
    private static readonly FeedId TestFeedId = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));

    #region GetReadPositionAsync Tests

    [Fact]
    public async Task GetReadPositionAsync_WhenCacheHit_ReturnsCachedValueWithoutQueryingDb()
    {
        // Arrange
        var (service, cacheService, repository, _) = CreateService();
        var expectedBlockIndex = new BlockIndex(500);

        cacheService
            .Setup(x => x.GetReadPositionAsync(TestUserId, TestFeedId))
            .ReturnsAsync(expectedBlockIndex);

        // Act
        var result = await service.GetReadPositionAsync(TestUserId, TestFeedId);

        // Assert
        result.Should().Be(expectedBlockIndex);
        repository.Verify(
            x => x.GetReadPositionAsync(It.IsAny<string>(), It.IsAny<FeedId>()),
            Times.Never);
    }

    [Fact]
    public async Task GetReadPositionAsync_WhenCacheMiss_QueriesDbAndPopulatesCache()
    {
        // Arrange
        var (service, cacheService, repository, unitOfWork) = CreateService();
        var expectedBlockIndex = new BlockIndex(500);

        cacheService
            .Setup(x => x.GetReadPositionAsync(TestUserId, TestFeedId))
            .ReturnsAsync((BlockIndex?)null);

        repository
            .Setup(x => x.GetReadPositionAsync(TestUserId, TestFeedId))
            .ReturnsAsync(expectedBlockIndex);

        unitOfWork
            .Setup(x => x.GetRepository<IFeedReadPositionRepository>())
            .Returns(repository.Object);

        cacheService
            .Setup(x => x.SetReadPositionAsync(TestUserId, TestFeedId, expectedBlockIndex))
            .ReturnsAsync(true);

        // Act
        var result = await service.GetReadPositionAsync(TestUserId, TestFeedId);

        // Assert
        result.Should().Be(expectedBlockIndex);
        repository.Verify(
            x => x.GetReadPositionAsync(TestUserId, TestFeedId),
            Times.Once);
        cacheService.Verify(
            x => x.SetReadPositionAsync(TestUserId, TestFeedId, expectedBlockIndex),
            Times.Once);
    }

    [Fact]
    public async Task GetReadPositionAsync_WhenNotFoundAnywhere_ReturnsZero()
    {
        // Arrange
        var (service, cacheService, repository, unitOfWork) = CreateService();

        cacheService
            .Setup(x => x.GetReadPositionAsync(TestUserId, TestFeedId))
            .ReturnsAsync((BlockIndex?)null);

        repository
            .Setup(x => x.GetReadPositionAsync(TestUserId, TestFeedId))
            .ReturnsAsync((BlockIndex?)null);

        unitOfWork
            .Setup(x => x.GetRepository<IFeedReadPositionRepository>())
            .Returns(repository.Object);

        // Act
        var result = await service.GetReadPositionAsync(TestUserId, TestFeedId);

        // Assert
        result.Value.Should().Be(0);
    }

    [Fact]
    public async Task GetReadPositionAsync_WhenUserIdIsNull_ReturnsZero()
    {
        // Arrange
        var (service, _, _, _) = CreateService();

        // Act
        var result = await service.GetReadPositionAsync(null!, TestFeedId);

        // Assert
        result.Value.Should().Be(0);
    }

    [Fact]
    public async Task GetReadPositionAsync_WhenUserIdIsEmpty_ReturnsZero()
    {
        // Arrange
        var (service, _, _, _) = CreateService();

        // Act
        var result = await service.GetReadPositionAsync(string.Empty, TestFeedId);

        // Assert
        result.Value.Should().Be(0);
    }

    #endregion

    #region GetReadPositionsForUserAsync Tests

    [Fact]
    public async Task GetReadPositionsForUserAsync_WhenCacheHit_ReturnsCachedValueWithoutQueryingDb()
    {
        // Arrange
        var (service, cacheService, repository, _) = CreateService();
        var cachedPositions = new Dictionary<FeedId, BlockIndex>
        {
            { TestFeedId, new BlockIndex(500) }
        };

        cacheService
            .Setup(x => x.GetReadPositionsForUserAsync(TestUserId))
            .ReturnsAsync(cachedPositions);

        // Act
        var result = await service.GetReadPositionsForUserAsync(TestUserId);

        // Assert
        result.Should().BeEquivalentTo(cachedPositions);
        repository.Verify(
            x => x.GetReadPositionsForUserAsync(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task GetReadPositionsForUserAsync_WhenCacheMiss_QueriesDbAndPopulatesCache()
    {
        // Arrange
        var (service, cacheService, repository, unitOfWork) = CreateService();
        var dbPositions = new Dictionary<FeedId, BlockIndex>
        {
            { TestFeedId, new BlockIndex(500) }
        };

        cacheService
            .Setup(x => x.GetReadPositionsForUserAsync(TestUserId))
            .ReturnsAsync((IReadOnlyDictionary<FeedId, BlockIndex>?)null);

        repository
            .Setup(x => x.GetReadPositionsForUserAsync(TestUserId))
            .ReturnsAsync(dbPositions);

        unitOfWork
            .Setup(x => x.GetRepository<IFeedReadPositionRepository>())
            .Returns(repository.Object);

        cacheService
            .Setup(x => x.SetReadPositionAsync(It.IsAny<string>(), It.IsAny<FeedId>(), It.IsAny<BlockIndex>()))
            .ReturnsAsync(true);

        // Act
        var result = await service.GetReadPositionsForUserAsync(TestUserId);

        // Assert
        result.Should().BeEquivalentTo(dbPositions);
        repository.Verify(
            x => x.GetReadPositionsForUserAsync(TestUserId),
            Times.Once);
        cacheService.Verify(
            x => x.SetReadPositionAsync(TestUserId, TestFeedId, new BlockIndex(500)),
            Times.Once);
    }

    [Fact]
    public async Task GetReadPositionsForUserAsync_WhenUserIdIsNull_ReturnsEmptyDictionary()
    {
        // Arrange
        var (service, _, _, _) = CreateService();

        // Act
        var result = await service.GetReadPositionsForUserAsync(null!);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region MarkFeedAsReadAsync Tests

    [Fact]
    public async Task MarkFeedAsReadAsync_WhenUpdateSucceeds_UpdatesDbAndCache()
    {
        // Arrange
        var (service, cacheService, repository, writableUnitOfWork) = CreateServiceWithWritable();
        var blockIndex = new BlockIndex(500);

        repository
            .Setup(x => x.UpsertReadPositionAsync(TestUserId, TestFeedId, blockIndex))
            .ReturnsAsync(true);

        writableUnitOfWork
            .Setup(x => x.GetRepository<IFeedReadPositionRepository>())
            .Returns(repository.Object);

        writableUnitOfWork
            .Setup(x => x.CommitAsync())
            .Returns(Task.CompletedTask);

        cacheService
            .Setup(x => x.SetReadPositionAsync(TestUserId, TestFeedId, blockIndex))
            .ReturnsAsync(true);

        // Act
        var result = await service.MarkFeedAsReadAsync(TestUserId, TestFeedId, blockIndex);

        // Assert
        result.Should().BeTrue();
        repository.Verify(
            x => x.UpsertReadPositionAsync(TestUserId, TestFeedId, blockIndex),
            Times.Once);
        writableUnitOfWork.Verify(x => x.CommitAsync(), Times.Once);
        cacheService.Verify(
            x => x.SetReadPositionAsync(TestUserId, TestFeedId, blockIndex),
            Times.Once);
    }

    [Fact]
    public async Task MarkFeedAsReadAsync_WhenMaxWinsRejects_DoesNotUpdateCache()
    {
        // Arrange
        var (service, cacheService, repository, writableUnitOfWork) = CreateServiceWithWritable();
        var blockIndex = new BlockIndex(100); // Lower than current

        repository
            .Setup(x => x.UpsertReadPositionAsync(TestUserId, TestFeedId, blockIndex))
            .ReturnsAsync(false); // Max wins rejected

        writableUnitOfWork
            .Setup(x => x.GetRepository<IFeedReadPositionRepository>())
            .Returns(repository.Object);

        writableUnitOfWork
            .Setup(x => x.CommitAsync())
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.MarkFeedAsReadAsync(TestUserId, TestFeedId, blockIndex);

        // Assert
        result.Should().BeFalse();
        cacheService.Verify(
            x => x.SetReadPositionAsync(It.IsAny<string>(), It.IsAny<FeedId>(), It.IsAny<BlockIndex>()),
            Times.Never);
    }

    [Fact]
    public async Task MarkFeedAsReadAsync_WhenCacheUpdateFails_StillReturnsTrue()
    {
        // Arrange
        var (service, cacheService, repository, writableUnitOfWork) = CreateServiceWithWritable();
        var blockIndex = new BlockIndex(500);

        repository
            .Setup(x => x.UpsertReadPositionAsync(TestUserId, TestFeedId, blockIndex))
            .ReturnsAsync(true);

        writableUnitOfWork
            .Setup(x => x.GetRepository<IFeedReadPositionRepository>())
            .Returns(repository.Object);

        writableUnitOfWork
            .Setup(x => x.CommitAsync())
            .Returns(Task.CompletedTask);

        cacheService
            .Setup(x => x.SetReadPositionAsync(TestUserId, TestFeedId, blockIndex))
            .ReturnsAsync(false); // Cache update failed

        // Act
        var result = await service.MarkFeedAsReadAsync(TestUserId, TestFeedId, blockIndex);

        // Assert
        result.Should().BeTrue(); // DB was updated, cache failure doesn't affect return value
    }

    [Fact]
    public async Task MarkFeedAsReadAsync_WhenUserIdIsNull_ReturnsFalse()
    {
        // Arrange
        var (service, _, _, _) = CreateServiceWithWritable();
        var blockIndex = new BlockIndex(500);

        // Act
        var result = await service.MarkFeedAsReadAsync(null!, TestFeedId, blockIndex);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task MarkFeedAsReadAsync_WhenUserIdIsEmpty_ReturnsFalse()
    {
        // Arrange
        var (service, _, _, _) = CreateServiceWithWritable();
        var blockIndex = new BlockIndex(500);

        // Act
        var result = await service.MarkFeedAsReadAsync(string.Empty, TestFeedId, blockIndex);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task MarkFeedAsReadAsync_WhenConcurrentUpdate_ReturnsTrue()
    {
        // Arrange
        // This test verifies that when multiple concurrent requests try to update the same
        // read position, the service gracefully handles DbUpdateConcurrencyException and
        // returns true (since another request already completed the update).
        var (service, cacheService, repository, writableUnitOfWork) = CreateServiceWithWritable();
        var blockIndex = new BlockIndex(500);

        repository
            .Setup(x => x.UpsertReadPositionAsync(TestUserId, TestFeedId, blockIndex))
            .ReturnsAsync(true);

        writableUnitOfWork
            .Setup(x => x.GetRepository<IFeedReadPositionRepository>())
            .Returns(repository.Object);

        // Simulate concurrent update exception on commit
        writableUnitOfWork
            .Setup(x => x.CommitAsync())
            .ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException(
                "Concurrent update detected"));

        // Act
        var result = await service.MarkFeedAsReadAsync(TestUserId, TestFeedId, blockIndex);

        // Assert
        result.Should().BeTrue(); // Should return true because the update was already done by another request
        cacheService.Verify(
            x => x.SetReadPositionAsync(It.IsAny<string>(), It.IsAny<FeedId>(), It.IsAny<BlockIndex>()),
            Times.Never); // Cache should not be updated when exception occurs
    }

    #endregion

    #region Helper Methods

    private static (
        FeedReadPositionStorageService service,
        Mock<IFeedReadPositionCacheService> cacheService,
        Mock<IFeedReadPositionRepository> repository,
        Mock<IReadOnlyUnitOfWork<FeedsDbContext>> unitOfWork)
        CreateService()
    {
        var mocker = new AutoMocker();

        var cacheService = mocker.GetMock<IFeedReadPositionCacheService>();
        var repository = new Mock<IFeedReadPositionRepository>();
        var unitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(unitOfWork.Object);

        var service = mocker.CreateInstance<FeedReadPositionStorageService>();

        return (service, cacheService, repository, unitOfWork);
    }

    private static (
        FeedReadPositionStorageService service,
        Mock<IFeedReadPositionCacheService> cacheService,
        Mock<IFeedReadPositionRepository> repository,
        Mock<IWritableUnitOfWork<FeedsDbContext>> writableUnitOfWork)
        CreateServiceWithWritable()
    {
        var mocker = new AutoMocker();

        var cacheService = mocker.GetMock<IFeedReadPositionCacheService>();
        var repository = new Mock<IFeedReadPositionRepository>();
        var writableUnitOfWork = new Mock<IWritableUnitOfWork<FeedsDbContext>>();

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateWritable())
            .Returns(writableUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedReadPositionStorageService>();

        return (service, cacheService, repository, writableUnitOfWork);
    }

    #endregion
}
