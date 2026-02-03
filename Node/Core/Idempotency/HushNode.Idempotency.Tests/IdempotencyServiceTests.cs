using FluentAssertions;
using HushNetwork.proto;
using HushNode.Feeds.Storage;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.AutoMock;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushNode.Idempotency.Tests;

/// <summary>
/// Unit tests for IdempotencyService.
/// FEAT-057: Server Message Idempotency.
/// Covers acceptance test scenarios F1-001, F1-002, F1-003.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class IdempotencyServiceTests
{
    private static FeedMessageId CreateMessageId() => new(Guid.NewGuid());

    #region CheckAsync Tests (F1-001, F1-002, F1-003)

    /// <summary>
    /// F1-001: New message with unique messageId is accepted.
    /// </summary>
    [Fact]
    public async Task CheckAsync_NewMessage_ReturnsAccepted()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();

        var mockRepository = new Mock<IFeedMessageRepository>();
        mockRepository
            .Setup(x => x.ExistsByMessageIdAsync(messageId))
            .ReturnsAsync(false);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<IdempotencyService>();

        // Act
        var result = await service.CheckAsync(messageId);

        // Assert
        result.Should().Be(TransactionStatus.Accepted);
        mockRepository.Verify(x => x.ExistsByMessageIdAsync(messageId), Times.Once);
    }

    /// <summary>
    /// F1-002: Duplicate messageId found in database returns ALREADY_EXISTS.
    /// </summary>
    [Fact]
    public async Task CheckAsync_DuplicateInDatabase_ReturnsAlreadyExists()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();

        var mockRepository = new Mock<IFeedMessageRepository>();
        mockRepository
            .Setup(x => x.ExistsByMessageIdAsync(messageId))
            .ReturnsAsync(true); // Message exists in DB

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<IdempotencyService>();

        // Act
        var result = await service.CheckAsync(messageId);

        // Assert
        result.Should().Be(TransactionStatus.AlreadyExists);
    }

    /// <summary>
    /// F1-003: Duplicate messageId found in MemPool returns PENDING.
    /// Database is NOT queried (early return).
    /// </summary>
    [Fact]
    public async Task CheckAsync_DuplicateInMemPool_ReturnsPending_DatabaseNotQueried()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();

        var mockRepository = new Mock<IFeedMessageRepository>();

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<IdempotencyService>();

        // First, track the message in MemPool
        service.TryTrackInMemPool(messageId);

        // Act
        var result = await service.CheckAsync(messageId);

        // Assert
        result.Should().Be(TransactionStatus.Pending);
        // Database should NOT be queried - verify it was never called
        mockRepository.Verify(x => x.ExistsByMessageIdAsync(It.IsAny<FeedMessageId>()), Times.Never);
    }

    /// <summary>
    /// Database error during check returns REJECTED (fail-closed).
    /// </summary>
    [Fact]
    public async Task CheckAsync_DatabaseError_ReturnsRejected()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();

        var mockRepository = new Mock<IFeedMessageRepository>();
        mockRepository
            .Setup(x => x.ExistsByMessageIdAsync(messageId))
            .ThrowsAsync(new Exception("Database connection failed"));

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<IdempotencyService>();

        // Act
        var result = await service.CheckAsync(messageId);

        // Assert
        result.Should().Be(TransactionStatus.Rejected);
    }

    #endregion

    #region TryTrackInMemPool Tests

    [Fact]
    public void TryTrackInMemPool_FirstCall_ReturnsTrue()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();
        var service = mocker.CreateInstance<IdempotencyService>();

        // Act
        var result = service.TryTrackInMemPool(messageId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TryTrackInMemPool_SecondCall_ReturnsFalse()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();
        var service = mocker.CreateInstance<IdempotencyService>();

        // First call succeeds
        service.TryTrackInMemPool(messageId);

        // Act - Second call for same ID
        var result = service.TryTrackInMemPool(messageId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void TryTrackInMemPool_DifferentIds_BothReturnTrue()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId1 = CreateMessageId();
        var messageId2 = CreateMessageId();
        var service = mocker.CreateInstance<IdempotencyService>();

        // Act
        var result1 = service.TryTrackInMemPool(messageId1);
        var result2 = service.TryTrackInMemPool(messageId2);

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeTrue();
    }

    #endregion

    #region RemoveFromTracking Tests

    [Fact]
    public void RemoveFromTracking_RemovesTrackedMessages()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();
        var service = mocker.CreateInstance<IdempotencyService>();

        // Track message first
        service.TryTrackInMemPool(messageId);

        // Act - Remove from tracking
        service.RemoveFromTracking(new[] { messageId });

        // Assert - Can track again (proves it was removed)
        var canTrackAgain = service.TryTrackInMemPool(messageId);
        canTrackAgain.Should().BeTrue();
    }

    [Fact]
    public void RemoveFromTracking_MultipleMessages_RemovesAll()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId1 = CreateMessageId();
        var messageId2 = CreateMessageId();
        var messageId3 = CreateMessageId();
        var service = mocker.CreateInstance<IdempotencyService>();

        // Track all messages
        service.TryTrackInMemPool(messageId1);
        service.TryTrackInMemPool(messageId2);
        service.TryTrackInMemPool(messageId3);

        // Act - Remove all
        service.RemoveFromTracking(new[] { messageId1, messageId2, messageId3 });

        // Assert - All can be tracked again
        service.TryTrackInMemPool(messageId1).Should().BeTrue();
        service.TryTrackInMemPool(messageId2).Should().BeTrue();
        service.TryTrackInMemPool(messageId3).Should().BeTrue();
    }

    [Fact]
    public void RemoveFromTracking_NonExistentId_NoError()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();
        var service = mocker.CreateInstance<IdempotencyService>();

        // Act - Remove non-existent ID (should not throw)
        var act = () => service.RemoveFromTracking(new[] { messageId });

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void RemoveFromTracking_EmptyCollection_NoError()
    {
        // Arrange
        var mocker = new AutoMocker();
        var service = mocker.CreateInstance<IdempotencyService>();

        // Act - Remove empty collection (should not throw)
        var act = () => service.RemoveFromTracking(Array.Empty<FeedMessageId>());

        // Assert
        act.Should().NotThrow();
    }

    #endregion
}
