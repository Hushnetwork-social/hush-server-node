using FluentAssertions;
using HushNode.Events;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using Moq;
using Olimpo;
using Xunit;

namespace HushNode.Caching.Tests;

/// <summary>
/// Tests for FeedParticipantsCacheEventHandler - handles group membership events for cache invalidation.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class FeedParticipantsCacheEventHandlerTests
{
    private static readonly FeedId TestFeedId = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private const string TestUserAddress = "test-user-address-123";

    #region UserJoinedGroupEvent Tests

    [Fact]
    public async Task HandleAsync_UserJoinedGroupEvent_AddsParticipantToCache()
    {
        // Arrange
        var (sut, cacheServiceMock, _) = CreateEventHandler();
        var evt = new UserJoinedGroupEvent(TestFeedId, TestUserAddress, new BlockIndex(100));

        cacheServiceMock
            .Setup(x => x.AddParticipantAsync(TestFeedId, TestUserAddress))
            .Returns(Task.CompletedTask);

        cacheServiceMock
            .Setup(x => x.InvalidateKeyGenerationsAsync(TestFeedId))
            .Returns(Task.CompletedTask);

        // Act
        await sut.HandleAsync(evt);

        // Assert
        cacheServiceMock.Verify(
            x => x.AddParticipantAsync(TestFeedId, TestUserAddress),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_UserJoinedGroupEvent_InvalidatesKeyGenerationsCache()
    {
        // Arrange
        var (sut, cacheServiceMock, _) = CreateEventHandler();
        var evt = new UserJoinedGroupEvent(TestFeedId, TestUserAddress, new BlockIndex(100));

        cacheServiceMock
            .Setup(x => x.AddParticipantAsync(TestFeedId, TestUserAddress))
            .Returns(Task.CompletedTask);

        cacheServiceMock
            .Setup(x => x.InvalidateKeyGenerationsAsync(TestFeedId))
            .Returns(Task.CompletedTask);

        // Act
        await sut.HandleAsync(evt);

        // Assert
        cacheServiceMock.Verify(
            x => x.InvalidateKeyGenerationsAsync(TestFeedId),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_UserJoinedGroupEvent_OnCacheFailure_DoesNotThrow()
    {
        // Arrange
        var (sut, cacheServiceMock, _) = CreateEventHandler();
        var evt = new UserJoinedGroupEvent(TestFeedId, TestUserAddress, new BlockIndex(100));

        cacheServiceMock
            .Setup(x => x.AddParticipantAsync(TestFeedId, TestUserAddress))
            .ThrowsAsync(new Exception("Cache error"));

        // Act
        var act = async () => await sut.HandleAsync(evt);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region UserLeftGroupEvent Tests

    [Fact]
    public async Task HandleAsync_UserLeftGroupEvent_RemovesParticipantFromCache()
    {
        // Arrange
        var (sut, cacheServiceMock, _) = CreateEventHandler();
        var evt = new UserLeftGroupEvent(TestFeedId, TestUserAddress, new BlockIndex(100));

        cacheServiceMock
            .Setup(x => x.RemoveParticipantAsync(TestFeedId, TestUserAddress))
            .Returns(Task.CompletedTask);

        cacheServiceMock
            .Setup(x => x.InvalidateKeyGenerationsAsync(TestFeedId))
            .Returns(Task.CompletedTask);

        // Act
        await sut.HandleAsync(evt);

        // Assert
        cacheServiceMock.Verify(
            x => x.RemoveParticipantAsync(TestFeedId, TestUserAddress),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_UserLeftGroupEvent_InvalidatesKeyGenerationsCache()
    {
        // Arrange
        var (sut, cacheServiceMock, _) = CreateEventHandler();
        var evt = new UserLeftGroupEvent(TestFeedId, TestUserAddress, new BlockIndex(100));

        cacheServiceMock
            .Setup(x => x.RemoveParticipantAsync(TestFeedId, TestUserAddress))
            .Returns(Task.CompletedTask);

        cacheServiceMock
            .Setup(x => x.InvalidateKeyGenerationsAsync(TestFeedId))
            .Returns(Task.CompletedTask);

        // Act
        await sut.HandleAsync(evt);

        // Assert
        cacheServiceMock.Verify(
            x => x.InvalidateKeyGenerationsAsync(TestFeedId),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_UserLeftGroupEvent_OnCacheFailure_DoesNotThrow()
    {
        // Arrange
        var (sut, cacheServiceMock, _) = CreateEventHandler();
        var evt = new UserLeftGroupEvent(TestFeedId, TestUserAddress, new BlockIndex(100));

        cacheServiceMock
            .Setup(x => x.RemoveParticipantAsync(TestFeedId, TestUserAddress))
            .ThrowsAsync(new Exception("Cache error"));

        // Act
        var act = async () => await sut.HandleAsync(evt);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region UserBannedFromGroupEvent Tests

    [Fact]
    public async Task HandleAsync_UserBannedFromGroupEvent_RemovesParticipantFromCache()
    {
        // Arrange
        var (sut, cacheServiceMock, _) = CreateEventHandler();
        var evt = new UserBannedFromGroupEvent(TestFeedId, TestUserAddress, new BlockIndex(100));

        cacheServiceMock
            .Setup(x => x.RemoveParticipantAsync(TestFeedId, TestUserAddress))
            .Returns(Task.CompletedTask);

        cacheServiceMock
            .Setup(x => x.InvalidateKeyGenerationsAsync(TestFeedId))
            .Returns(Task.CompletedTask);

        // Act
        await sut.HandleAsync(evt);

        // Assert
        cacheServiceMock.Verify(
            x => x.RemoveParticipantAsync(TestFeedId, TestUserAddress),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_UserBannedFromGroupEvent_InvalidatesKeyGenerationsCache()
    {
        // Arrange
        var (sut, cacheServiceMock, _) = CreateEventHandler();
        var evt = new UserBannedFromGroupEvent(TestFeedId, TestUserAddress, new BlockIndex(100));

        cacheServiceMock
            .Setup(x => x.RemoveParticipantAsync(TestFeedId, TestUserAddress))
            .Returns(Task.CompletedTask);

        cacheServiceMock
            .Setup(x => x.InvalidateKeyGenerationsAsync(TestFeedId))
            .Returns(Task.CompletedTask);

        // Act
        await sut.HandleAsync(evt);

        // Assert
        cacheServiceMock.Verify(
            x => x.InvalidateKeyGenerationsAsync(TestFeedId),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_UserBannedFromGroupEvent_OnCacheFailure_DoesNotThrow()
    {
        // Arrange
        var (sut, cacheServiceMock, _) = CreateEventHandler();
        var evt = new UserBannedFromGroupEvent(TestFeedId, TestUserAddress, new BlockIndex(100));

        cacheServiceMock
            .Setup(x => x.RemoveParticipantAsync(TestFeedId, TestUserAddress))
            .ThrowsAsync(new Exception("Cache error"));

        // Act
        var act = async () => await sut.HandleAsync(evt);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region EventAggregator Subscription Tests

    [Fact]
    public void Constructor_SubscribesToEventAggregator()
    {
        // Arrange
        var cacheServiceMock = new Mock<IFeedParticipantsCacheService>();
        var eventAggregatorMock = new Mock<IEventAggregator>();
        var loggerMock = new Mock<ILogger<FeedParticipantsCacheEventHandler>>();

        // Act
        var sut = new FeedParticipantsCacheEventHandler(
            cacheServiceMock.Object,
            eventAggregatorMock.Object,
            loggerMock.Object);

        // Assert
        eventAggregatorMock.Verify(
            x => x.Subscribe(sut),
            Times.Once);
    }

    #endregion

    #region Helper Methods

    private static (FeedParticipantsCacheEventHandler sut, Mock<IFeedParticipantsCacheService> cacheServiceMock, Mock<IEventAggregator> eventAggregatorMock)
        CreateEventHandler()
    {
        var cacheServiceMock = new Mock<IFeedParticipantsCacheService>();
        var eventAggregatorMock = new Mock<IEventAggregator>();
        var loggerMock = new Mock<ILogger<FeedParticipantsCacheEventHandler>>();

        var sut = new FeedParticipantsCacheEventHandler(
            cacheServiceMock.Object,
            eventAggregatorMock.Object,
            loggerMock.Object);

        return (sut, cacheServiceMock, eventAggregatorMock);
    }

    #endregion
}
