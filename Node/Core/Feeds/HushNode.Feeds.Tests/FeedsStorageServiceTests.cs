using FluentAssertions;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Feeds.Model;
using Moq;
using Moq.AutoMock;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for FeedsStorageService storage methods.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class FeedsStorageServiceTests
{
    #region GetMaxKeyGenerationAsync Tests

    [Fact]
    public async Task GetMaxKeyGenerationAsync_WithMultipleKeyGenerations_ShouldReturnMax()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var expectedMax = 5;

        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(expectedMax);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.GetMaxKeyGenerationAsync(feedId);

        // Assert
        result.Should().Be(expectedMax);
    }

    [Fact]
    public async Task GetMaxKeyGenerationAsync_WithSingleKeyGeneration_ShouldReturnZero()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();

        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(0);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.GetMaxKeyGenerationAsync(feedId);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task GetMaxKeyGenerationAsync_WithNonExistentGroup_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();

        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync((int?)null);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        var result = await service.GetMaxKeyGenerationAsync(feedId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetMaxKeyGenerationAsync_ShouldUseReadOnlyUnitOfWork()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();

        var mockRepository = new Mock<IFeedsRepository>();
        mockRepository
            .Setup(x => x.GetMaxKeyGenerationAsync(feedId))
            .ReturnsAsync(2);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedsRepository>())
            .Returns(mockRepository.Object);

        var mockProvider = mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>();
        mockProvider
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedsStorageService>();

        // Act
        await service.GetMaxKeyGenerationAsync(feedId);

        // Assert
        mockProvider.Verify(x => x.CreateReadOnly(), Times.Once);
        mockRepository.Verify(x => x.GetMaxKeyGenerationAsync(feedId), Times.Once);
    }

    #endregion
}
