using FluentAssertions;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Moq;
using Moq.AutoMock;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// FEAT-052: Unit tests for message pagination functionality.
/// Tests the FeedMessageStorageService pagination method using mocked repository.
/// </summary>
public class FeedMessagePaginationTests
{
    #region GetPaginatedMessagesAsync Basic Tests

    [Fact]
    public async Task GetPaginatedMessagesAsync_WithLimit_ShouldReturnLimitedMessages()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var messages = TestDataFactory.CreateFeedMessagesInRange(feedId, 1, 100);
        var limit = 50;

        var paginatedResult = new PaginatedMessagesResult(
            Messages: messages.Take(limit).ToList(),
            HasMoreMessages: true,
            OldestBlockIndex: new BlockIndex(1));

        var mockRepository = new Mock<IFeedMessageRepository>();
        mockRepository
            .Setup(x => x.GetPaginatedMessagesAsync(feedId, It.IsAny<BlockIndex>(), limit, false, It.IsAny<BlockIndex?>()))
            .ReturnsAsync(paginatedResult);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedMessageStorageService>();

        // Act
        var result = await service.GetPaginatedMessagesAsync(feedId, new BlockIndex(0), limit, fetchLatest: false);

        // Assert
        result.Messages.Should().HaveCount(limit);
        result.HasMoreMessages.Should().BeTrue();
        result.OldestBlockIndex.Should().Be(new BlockIndex(1));
    }

    [Fact]
    public async Task GetPaginatedMessagesAsync_WithFetchLatest_ShouldReturnLatestMessages()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var limit = 20;

        // Simulate messages from blocks 481-500 (latest 20 out of 500)
        var latestMessages = TestDataFactory.CreateFeedMessagesInRange(feedId, 481, 500);

        var paginatedResult = new PaginatedMessagesResult(
            Messages: latestMessages,
            HasMoreMessages: true, // Messages 1-480 exist
            OldestBlockIndex: new BlockIndex(481));

        var mockRepository = new Mock<IFeedMessageRepository>();
        mockRepository
            .Setup(x => x.GetPaginatedMessagesAsync(feedId, It.IsAny<BlockIndex>(), limit, true, It.IsAny<BlockIndex?>()))
            .ReturnsAsync(paginatedResult);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedMessageStorageService>();

        // Act
        var result = await service.GetPaginatedMessagesAsync(feedId, new BlockIndex(0), limit, fetchLatest: true);

        // Assert
        result.Messages.Should().HaveCount(20);
        result.HasMoreMessages.Should().BeTrue();
        result.OldestBlockIndex.Should().Be(new BlockIndex(481));
    }

    [Fact]
    public async Task GetPaginatedMessagesAsync_WithSinceBlockIndex_ShouldRespectInclusive()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var sinceBlockIndex = new BlockIndex(50);
        var limit = 100;

        // Messages from blocks 50-100 (inclusive of sinceBlockIndex)
        var messages = TestDataFactory.CreateFeedMessagesInRange(feedId, 50, 100);

        var paginatedResult = new PaginatedMessagesResult(
            Messages: messages,
            HasMoreMessages: true, // Messages 1-49 exist
            OldestBlockIndex: new BlockIndex(50));

        var mockRepository = new Mock<IFeedMessageRepository>();
        mockRepository
            .Setup(x => x.GetPaginatedMessagesAsync(feedId, sinceBlockIndex, limit, false, It.IsAny<BlockIndex?>()))
            .ReturnsAsync(paginatedResult);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedMessageStorageService>();

        // Act
        var result = await service.GetPaginatedMessagesAsync(feedId, sinceBlockIndex, limit, fetchLatest: false);

        // Assert
        result.Messages.Should().HaveCount(51); // 50 to 100 inclusive
        result.OldestBlockIndex.Should().Be(new BlockIndex(50));
        mockRepository.Verify(x => x.GetPaginatedMessagesAsync(feedId, sinceBlockIndex, limit, false, It.IsAny<BlockIndex?>()), Times.Once);
    }

    #endregion

    #region Empty Feed Tests

    [Fact]
    public async Task GetPaginatedMessagesAsync_EmptyFeed_ShouldReturnEmptyResult()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();

        var paginatedResult = new PaginatedMessagesResult(
            Messages: new List<FeedMessage>(),
            HasMoreMessages: false,
            OldestBlockIndex: new BlockIndex(0));

        var mockRepository = new Mock<IFeedMessageRepository>();
        mockRepository
            .Setup(x => x.GetPaginatedMessagesAsync(feedId, It.IsAny<BlockIndex>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<BlockIndex?>()))
            .ReturnsAsync(paginatedResult);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedMessageStorageService>();

        // Act
        var result = await service.GetPaginatedMessagesAsync(feedId, new BlockIndex(0), 100, fetchLatest: false);

        // Assert
        result.Messages.Should().BeEmpty();
        result.HasMoreMessages.Should().BeFalse();
        result.OldestBlockIndex.Should().Be(new BlockIndex(0));
    }

    [Fact]
    public async Task GetPaginatedMessagesAsync_EmptyFeedWithFetchLatest_ShouldReturnEmptyResult()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();

        var paginatedResult = new PaginatedMessagesResult(
            Messages: new List<FeedMessage>(),
            HasMoreMessages: false,
            OldestBlockIndex: new BlockIndex(0));

        var mockRepository = new Mock<IFeedMessageRepository>();
        mockRepository
            .Setup(x => x.GetPaginatedMessagesAsync(feedId, It.IsAny<BlockIndex>(), It.IsAny<int>(), true, It.IsAny<BlockIndex?>()))
            .ReturnsAsync(paginatedResult);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedMessageStorageService>();

        // Act
        var result = await service.GetPaginatedMessagesAsync(feedId, new BlockIndex(0), 100, fetchLatest: true);

        // Assert
        result.Messages.Should().BeEmpty();
        result.HasMoreMessages.Should().BeFalse();
        result.OldestBlockIndex.Should().Be(new BlockIndex(0));
    }

    #endregion

    #region HasMoreMessages Tests

    [Fact]
    public async Task GetPaginatedMessagesAsync_AllMessagesReturned_ShouldSetHasMoreToFalse()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var messages = TestDataFactory.CreateFeedMessagesInRange(feedId, 1, 50);

        var paginatedResult = new PaginatedMessagesResult(
            Messages: messages,
            HasMoreMessages: false, // No older messages exist
            OldestBlockIndex: new BlockIndex(1));

        var mockRepository = new Mock<IFeedMessageRepository>();
        mockRepository
            .Setup(x => x.GetPaginatedMessagesAsync(feedId, It.IsAny<BlockIndex>(), 100, false, It.IsAny<BlockIndex?>()))
            .ReturnsAsync(paginatedResult);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedMessageStorageService>();

        // Act
        var result = await service.GetPaginatedMessagesAsync(feedId, new BlockIndex(0), 100, fetchLatest: false);

        // Assert
        result.Messages.Should().HaveCount(50);
        result.HasMoreMessages.Should().BeFalse();
    }

    [Fact]
    public async Task GetPaginatedMessagesAsync_MoreMessagesExist_ShouldSetHasMoreToTrue()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var limit = 100;
        var messages = TestDataFactory.CreateFeedMessagesInRange(feedId, 101, 200);

        var paginatedResult = new PaginatedMessagesResult(
            Messages: messages,
            HasMoreMessages: true, // Messages 1-100 exist
            OldestBlockIndex: new BlockIndex(101));

        var mockRepository = new Mock<IFeedMessageRepository>();
        mockRepository
            .Setup(x => x.GetPaginatedMessagesAsync(feedId, new BlockIndex(101), limit, false, It.IsAny<BlockIndex?>()))
            .ReturnsAsync(paginatedResult);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedMessageStorageService>();

        // Act
        var result = await service.GetPaginatedMessagesAsync(feedId, new BlockIndex(101), limit, fetchLatest: false);

        // Assert
        result.HasMoreMessages.Should().BeTrue();
        result.OldestBlockIndex.Should().Be(new BlockIndex(101));
    }

    #endregion

    #region Unit of Work Pattern Tests

    [Fact]
    public async Task GetPaginatedMessagesAsync_ShouldUseReadOnlyUnitOfWork()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();

        var paginatedResult = new PaginatedMessagesResult(
            Messages: new List<FeedMessage>(),
            HasMoreMessages: false,
            OldestBlockIndex: new BlockIndex(0));

        var mockRepository = new Mock<IFeedMessageRepository>();
        mockRepository
            .Setup(x => x.GetPaginatedMessagesAsync(It.IsAny<FeedId>(), It.IsAny<BlockIndex>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<BlockIndex?>()))
            .ReturnsAsync(paginatedResult);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        var mockProvider = mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>();
        mockProvider
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<FeedMessageStorageService>();

        // Act
        await service.GetPaginatedMessagesAsync(feedId, new BlockIndex(0), 100, false);

        // Assert
        mockProvider.Verify(x => x.CreateReadOnly(), Times.Once);
        mockRepository.Verify(x => x.GetPaginatedMessagesAsync(feedId, new BlockIndex(0), 100, false, It.IsAny<BlockIndex?>()), Times.Once);
    }

    #endregion
}
