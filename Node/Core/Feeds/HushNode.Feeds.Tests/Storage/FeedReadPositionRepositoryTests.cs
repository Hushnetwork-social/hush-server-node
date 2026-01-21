using FluentAssertions;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Xunit;

namespace HushNode.Feeds.Tests.Storage;

/// <summary>
/// Tests for FeedReadPositionRepository using in-memory database.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class FeedReadPositionRepositoryTests : IClassFixture<FeedsInMemoryDbContextFixture>
{
    private readonly FeedsInMemoryDbContextFixture _fixture;

    public FeedReadPositionRepositoryTests(FeedsInMemoryDbContextFixture fixture)
    {
        _fixture = fixture;
    }

    private FeedReadPositionRepository CreateRepository(FeedsDbContext context)
    {
        var repository = new FeedReadPositionRepository();
        repository.SetContext(context);
        return repository;
    }

    #region GetReadPositionAsync Tests

    [Fact]
    public async Task GetReadPositionAsync_WhenExists_ShouldReturnValue()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var userId = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();
        var blockIndex = new BlockIndex(500);

        var entity = new FeedReadPositionEntity(userId, feedId, blockIndex, DateTime.UtcNow);
        await context.FeedReadPositions.AddAsync(entity);
        await context.SaveChangesAsync();

        // Act
        var result = await repository.GetReadPositionAsync(userId, feedId);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Should().Be(500);
    }

    [Fact]
    public async Task GetReadPositionAsync_WhenNotExists_ShouldReturnNull()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var userId = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        // Act
        var result = await repository.GetReadPositionAsync(userId, feedId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetReadPositionAsync_WithNullUserId_ShouldReturnNull()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var feedId = TestDataFactory.CreateFeedId();

        // Act
        var result = await repository.GetReadPositionAsync(null!, feedId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetReadPositionAsync_WithEmptyUserId_ShouldReturnNull()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var feedId = TestDataFactory.CreateFeedId();

        // Act
        var result = await repository.GetReadPositionAsync("", feedId);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetReadPositionsForUserAsync Tests

    [Fact]
    public async Task GetReadPositionsForUserAsync_WhenUserHasPositions_ShouldReturnAll()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var userId = TestDataFactory.CreateAddress();
        var feedId1 = TestDataFactory.CreateFeedId();
        var feedId2 = TestDataFactory.CreateFeedId();
        var feedId3 = TestDataFactory.CreateFeedId();

        await context.FeedReadPositions.AddRangeAsync(
            new FeedReadPositionEntity(userId, feedId1, new BlockIndex(100), DateTime.UtcNow),
            new FeedReadPositionEntity(userId, feedId2, new BlockIndex(200), DateTime.UtcNow),
            new FeedReadPositionEntity(userId, feedId3, new BlockIndex(300), DateTime.UtcNow)
        );
        await context.SaveChangesAsync();

        // Act
        var result = await repository.GetReadPositionsForUserAsync(userId);

        // Assert
        result.Should().HaveCount(3);
        result[feedId1].Value.Should().Be(100);
        result[feedId2].Value.Should().Be(200);
        result[feedId3].Value.Should().Be(300);
    }

    [Fact]
    public async Task GetReadPositionsForUserAsync_WhenUserHasNoPositions_ShouldReturnEmptyDictionary()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var userId = TestDataFactory.CreateAddress();

        // Act
        var result = await repository.GetReadPositionsForUserAsync(userId);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetReadPositionsForUserAsync_WithNullUserId_ShouldReturnEmptyDictionary()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        // Act
        var result = await repository.GetReadPositionsForUserAsync(null!);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region UpsertReadPositionAsync Tests

    [Fact]
    public async Task UpsertReadPositionAsync_WhenNotExists_ShouldCreateNewRecord()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var userId = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();
        var blockIndex = new BlockIndex(100);

        // Act
        var result = await repository.UpsertReadPositionAsync(userId, feedId, blockIndex);
        await context.SaveChangesAsync();

        // Assert
        result.Should().BeTrue();
        var retrieved = await repository.GetReadPositionAsync(userId, feedId);
        retrieved.Should().NotBeNull();
        retrieved!.Value.Should().Be(100);
    }

    [Fact]
    public async Task UpsertReadPositionAsync_WithHigherValue_ShouldUpdate()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var userId = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        // Insert initial value
        var entity = new FeedReadPositionEntity(userId, feedId, new BlockIndex(500), DateTime.UtcNow);
        await context.FeedReadPositions.AddAsync(entity);
        await context.SaveChangesAsync();

        // Act - Update with higher value
        var result = await repository.UpsertReadPositionAsync(userId, feedId, new BlockIndex(800));
        await context.SaveChangesAsync();

        // Assert
        result.Should().BeTrue();
        var retrieved = await repository.GetReadPositionAsync(userId, feedId);
        retrieved!.Value.Should().Be(800);
    }

    [Fact]
    public async Task UpsertReadPositionAsync_WithLowerValue_ShouldNotUpdate_MaxWins()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var userId = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        // Insert initial value of 500
        var entity = new FeedReadPositionEntity(userId, feedId, new BlockIndex(500), DateTime.UtcNow);
        await context.FeedReadPositions.AddAsync(entity);
        await context.SaveChangesAsync();

        // Act - Attempt to update with lower value (300)
        var result = await repository.UpsertReadPositionAsync(userId, feedId, new BlockIndex(300));
        await context.SaveChangesAsync();

        // Assert - Should return false and value should remain 500 (max wins)
        result.Should().BeFalse();
        var retrieved = await repository.GetReadPositionAsync(userId, feedId);
        retrieved!.Value.Should().Be(500);
    }

    [Fact]
    public async Task UpsertReadPositionAsync_WithEqualValue_ShouldNotUpdate()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var userId = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        // Insert initial value of 500
        var entity = new FeedReadPositionEntity(userId, feedId, new BlockIndex(500), DateTime.UtcNow);
        await context.FeedReadPositions.AddAsync(entity);
        await context.SaveChangesAsync();

        // Act - Attempt to update with same value
        var result = await repository.UpsertReadPositionAsync(userId, feedId, new BlockIndex(500));
        await context.SaveChangesAsync();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpsertReadPositionAsync_WithNullUserId_ShouldReturnFalse()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var feedId = TestDataFactory.CreateFeedId();

        // Act
        var result = await repository.UpsertReadPositionAsync(null!, feedId, new BlockIndex(100));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpsertReadPositionAsync_ShouldUpdateTimestamp()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var userId = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();
        var oldTime = DateTime.UtcNow.AddDays(-1);

        // Insert initial value with old timestamp
        var entity = new FeedReadPositionEntity(userId, feedId, new BlockIndex(100), oldTime);
        await context.FeedReadPositions.AddAsync(entity);
        await context.SaveChangesAsync();

        // Act - Update with higher value
        await repository.UpsertReadPositionAsync(userId, feedId, new BlockIndex(200));
        await context.SaveChangesAsync();

        // Assert - UpdatedAt should be recent (within last minute)
        var retrieved = await context.FeedReadPositions.FindAsync(userId, feedId);
        retrieved!.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    #endregion
}
