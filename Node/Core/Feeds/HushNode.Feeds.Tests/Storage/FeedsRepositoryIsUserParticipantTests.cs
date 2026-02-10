using FluentAssertions;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Xunit;

namespace HushNode.Feeds.Tests.Storage;

/// <summary>
/// Tests for FeedsRepository.IsUserParticipantOfFeedAsync using in-memory database.
/// FEAT-059: Per-Feed Pagination Authorization - Tests for participant checking.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class FeedsRepositoryIsUserParticipantTests : IClassFixture<FeedsInMemoryDbContextFixture>
{
    private readonly FeedsInMemoryDbContextFixture _fixture;

    public FeedsRepositoryIsUserParticipantTests(FeedsInMemoryDbContextFixture fixture)
    {
        _fixture = fixture;
    }

    private FeedsRepository CreateRepository(FeedsDbContext context)
    {
        var repository = new FeedsRepository();
        repository.SetContext(context);
        return repository;
    }

    #region Regular Feed (Chat) Participant Tests

    [Fact]
    public async Task IsUserParticipantOfFeedAsync_WhenUserIsRegularFeedParticipant_ShouldReturnTrue()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var feedId = TestDataFactory.CreateFeedId();
        var userAddress = TestDataFactory.CreateAddress();

        var feed = TestDataFactory.CreateChatFeedWithParticipant(feedId, userAddress);

        await context.Feeds.AddAsync(feed);
        await context.SaveChangesAsync();

        // Act
        var result = await repository.IsUserParticipantOfFeedAsync(feedId, userAddress);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsUserParticipantOfFeedAsync_WhenUserIsNotRegularFeedParticipant_ShouldReturnFalse()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var feedId = TestDataFactory.CreateFeedId();
        var participantAddress = TestDataFactory.CreateAddress();
        var nonParticipantAddress = TestDataFactory.CreateAddress();

        var feed = TestDataFactory.CreateChatFeedWithParticipant(feedId, participantAddress);

        await context.Feeds.AddAsync(feed);
        await context.SaveChangesAsync();

        // Act
        var result = await repository.IsUserParticipantOfFeedAsync(feedId, nonParticipantAddress);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Group Feed Participant Tests

    [Fact]
    public async Task IsUserParticipantOfFeedAsync_WhenUserIsActiveGroupParticipant_ShouldReturnTrue()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var feedId = TestDataFactory.CreateFeedId();
        var userAddress = TestDataFactory.CreateAddress();

        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var participant = TestDataFactory.CreateParticipantEntity(feedId, userAddress, ParticipantType.Member);
        groupFeed.Participants = new List<GroupFeedParticipantEntity> { participant };

        await context.GroupFeeds.AddAsync(groupFeed);
        await context.SaveChangesAsync();

        // Act
        var result = await repository.IsUserParticipantOfFeedAsync(feedId, userAddress);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsUserParticipantOfFeedAsync_WhenUserIsGroupAdmin_ShouldReturnTrue()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var feedId = TestDataFactory.CreateFeedId();
        var userAddress = TestDataFactory.CreateAddress();

        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var participant = TestDataFactory.CreateParticipantEntity(feedId, userAddress, ParticipantType.Admin);
        groupFeed.Participants = new List<GroupFeedParticipantEntity> { participant };

        await context.GroupFeeds.AddAsync(groupFeed);
        await context.SaveChangesAsync();

        // Act
        var result = await repository.IsUserParticipantOfFeedAsync(feedId, userAddress);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsUserParticipantOfFeedAsync_WhenGroupParticipantHasLeft_ShouldReturnFalse()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var feedId = TestDataFactory.CreateFeedId();
        var userAddress = TestDataFactory.CreateAddress();

        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var participant = TestDataFactory.CreateParticipantEntityWithHistory(
            feedId,
            userAddress,
            ParticipantType.Member,
            leftAtBlock: new BlockIndex(150),
            lastLeaveBlock: new BlockIndex(150));
        groupFeed.Participants = new List<GroupFeedParticipantEntity> { participant };

        await context.GroupFeeds.AddAsync(groupFeed);
        await context.SaveChangesAsync();

        // Act
        var result = await repository.IsUserParticipantOfFeedAsync(feedId, userAddress);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsUserParticipantOfFeedAsync_WhenGroupParticipantIsBanned_ShouldReturnFalse()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var feedId = TestDataFactory.CreateFeedId();
        var userAddress = TestDataFactory.CreateAddress();

        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var participant = TestDataFactory.CreateParticipantEntityWithHistory(
            feedId,
            userAddress,
            ParticipantType.Banned,
            leftAtBlock: null); // Banned users have LeftAtBlock null but ParticipantType.Banned

        groupFeed.Participants = new List<GroupFeedParticipantEntity> { participant };

        await context.GroupFeeds.AddAsync(groupFeed);
        await context.SaveChangesAsync();

        // Act
        var result = await repository.IsUserParticipantOfFeedAsync(feedId, userAddress);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsUserParticipantOfFeedAsync_WhenUserIsNotGroupParticipant_ShouldReturnFalse()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var feedId = TestDataFactory.CreateFeedId();
        var participantAddress = TestDataFactory.CreateAddress();
        var nonParticipantAddress = TestDataFactory.CreateAddress();

        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var participant = TestDataFactory.CreateParticipantEntity(feedId, participantAddress, ParticipantType.Member);
        groupFeed.Participants = new List<GroupFeedParticipantEntity> { participant };

        await context.GroupFeeds.AddAsync(groupFeed);
        await context.SaveChangesAsync();

        // Act
        var result = await repository.IsUserParticipantOfFeedAsync(feedId, nonParticipantAddress);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task IsUserParticipantOfFeedAsync_WhenFeedDoesNotExist_ShouldReturnFalse()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var nonExistentFeedId = TestDataFactory.CreateFeedId();
        var userAddress = TestDataFactory.CreateAddress();

        // Act
        var result = await repository.IsUserParticipantOfFeedAsync(nonExistentFeedId, userAddress);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsUserParticipantOfFeedAsync_WhenEmptyDatabase_ShouldReturnFalse()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var feedId = TestDataFactory.CreateFeedId();
        var userAddress = TestDataFactory.CreateAddress();

        // Act
        var result = await repository.IsUserParticipantOfFeedAsync(feedId, userAddress);

        // Assert
        result.Should().BeFalse();
    }

    #endregion
}
