using FluentAssertions;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Xunit;

namespace HushNode.Feeds.Tests.Storage;

/// <summary>
/// Tests for FeedMessageRepository using in-memory database.
/// FEAT-057: Server Message Idempotency - Tests for ExistsByMessageIdAsync.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class FeedMessageRepositoryTests : IClassFixture<FeedsInMemoryDbContextFixture>
{
    private readonly FeedsInMemoryDbContextFixture _fixture;

    public FeedMessageRepositoryTests(FeedsInMemoryDbContextFixture fixture)
    {
        _fixture = fixture;
    }

    private FeedMessageRepository CreateRepository(FeedsDbContext context)
    {
        var repository = new FeedMessageRepository();
        repository.SetContext(context);
        return repository;
    }

    #region ExistsByMessageIdAsync Tests

    [Fact]
    public async Task ExistsByMessageIdAsync_WhenMessageExists_ShouldReturnTrue()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var feedId = TestDataFactory.CreateFeedId();
        var messageId = TestDataFactory.CreateFeedMessageId();
        var blockIndex = new BlockIndex(100);

        var message = TestDataFactory.CreateFeedMessage(feedId, blockIndex, messageId: messageId);
        await context.FeedMessages.AddAsync(message);
        await context.SaveChangesAsync();

        // Act
        var result = await repository.ExistsByMessageIdAsync(messageId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsByMessageIdAsync_WhenMessageDoesNotExist_ShouldReturnFalse()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var nonExistentMessageId = TestDataFactory.CreateFeedMessageId();

        // Act
        var result = await repository.ExistsByMessageIdAsync(nonExistentMessageId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsByMessageIdAsync_WithEmptyDatabase_ShouldReturnFalse()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var messageId = TestDataFactory.CreateFeedMessageId();

        // Act
        var result = await repository.ExistsByMessageIdAsync(messageId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsByMessageIdAsync_WithMultipleMessages_ShouldFindCorrectOne()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var feedId = TestDataFactory.CreateFeedId();
        var targetMessageId = TestDataFactory.CreateFeedMessageId();

        // Add multiple messages including the target
        var messages = new[]
        {
            TestDataFactory.CreateFeedMessage(feedId, new BlockIndex(100)),
            TestDataFactory.CreateFeedMessage(feedId, new BlockIndex(101), messageId: targetMessageId),
            TestDataFactory.CreateFeedMessage(feedId, new BlockIndex(102)),
        };

        await context.FeedMessages.AddRangeAsync(messages);
        await context.SaveChangesAsync();

        // Act
        var result = await repository.ExistsByMessageIdAsync(targetMessageId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsByMessageIdAsync_WithMessagesInDifferentFeeds_ShouldFindAcrossFeeds()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var feedId1 = TestDataFactory.CreateFeedId();
        var feedId2 = TestDataFactory.CreateFeedId();
        var targetMessageId = TestDataFactory.CreateFeedMessageId();

        // Add messages in different feeds
        var messages = new[]
        {
            TestDataFactory.CreateFeedMessage(feedId1, new BlockIndex(100)),
            TestDataFactory.CreateFeedMessage(feedId2, new BlockIndex(101), messageId: targetMessageId),
        };

        await context.FeedMessages.AddRangeAsync(messages);
        await context.SaveChangesAsync();

        // Act
        var result = await repository.ExistsByMessageIdAsync(targetMessageId);

        // Assert
        result.Should().BeTrue();
    }

    #endregion
}
