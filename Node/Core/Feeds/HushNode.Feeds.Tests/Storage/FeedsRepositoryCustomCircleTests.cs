using FluentAssertions;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Xunit;

namespace HushNode.Feeds.Tests.Storage;

public class FeedsRepositoryCustomCircleTests : IClassFixture<FeedsInMemoryDbContextFixture>
{
    private readonly FeedsInMemoryDbContextFixture _fixture;

    public FeedsRepositoryCustomCircleTests(FeedsInMemoryDbContextFixture fixture)
    {
        _fixture = fixture;
    }

    private static FeedsRepository CreateRepository(FeedsDbContext context)
    {
        var repository = new FeedsRepository();
        repository.SetContext(context);
        return repository;
    }

    [Fact]
    public async Task GetCustomCircleCountByOwnerAsync_ShouldExcludeInnerCircleAndDeleted()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var owner = TestDataFactory.CreateAddress();

        var innerCircle = new GroupFeed(
            TestDataFactory.CreateFeedId(),
            "Inner Circle",
            "default",
            false,
            new BlockIndex(1),
            0,
            IsInnerCircle: true,
            OwnerPublicAddress: owner);

        var customA = new GroupFeed(
            TestDataFactory.CreateFeedId(),
            "Friends",
            "custom",
            false,
            new BlockIndex(2),
            0,
            IsInnerCircle: false,
            OwnerPublicAddress: owner);

        var customDeleted = new GroupFeed(
            TestDataFactory.CreateFeedId(),
            "Work",
            "custom",
            false,
            new BlockIndex(3),
            0,
            IsInnerCircle: false,
            OwnerPublicAddress: owner)
        {
            IsDeleted = true
        };

        await context.GroupFeeds.AddRangeAsync(innerCircle, customA, customDeleted);
        await context.SaveChangesAsync();

        // Act
        var count = await repository.GetCustomCircleCountByOwnerAsync(owner);

        // Assert
        count.Should().Be(1);
    }

    [Fact]
    public async Task OwnerHasCustomCircleNamedAsync_ShouldMatchCaseInsensitiveAndTrimmed()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var owner = TestDataFactory.CreateAddress();

        await context.GroupFeeds.AddAsync(new GroupFeed(
            TestDataFactory.CreateFeedId(),
            "Close Friends",
            "custom",
            false,
            new BlockIndex(1),
            0,
            IsInnerCircle: false,
            OwnerPublicAddress: owner));
        await context.SaveChangesAsync();

        // Act
        var exists = await repository.OwnerHasCustomCircleNamedAsync(owner, "  close friends ");

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task GetCirclesForOwnerAsync_ShouldReturnInnerFirstThenByMemberCount()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var owner = TestDataFactory.CreateAddress();

        var inner = new GroupFeed(
            TestDataFactory.CreateFeedId(),
            "Inner Circle",
            "default",
            false,
            new BlockIndex(10),
            1,
            IsInnerCircle: true,
            OwnerPublicAddress: owner);
        inner.Participants = new List<GroupFeedParticipantEntity>
        {
            TestDataFactory.CreateParticipantEntity(inner.FeedId, owner, ParticipantType.Owner),
            TestDataFactory.CreateParticipantEntity(inner.FeedId, TestDataFactory.CreateAddress(), ParticipantType.Member),
            TestDataFactory.CreateParticipantEntity(inner.FeedId, TestDataFactory.CreateAddress(), ParticipantType.Member),
        };

        var customBigger = new GroupFeed(
            TestDataFactory.CreateFeedId(),
            "Friends",
            "custom",
            false,
            new BlockIndex(11),
            1,
            IsInnerCircle: false,
            OwnerPublicAddress: owner);
        customBigger.Participants = new List<GroupFeedParticipantEntity>
        {
            TestDataFactory.CreateParticipantEntity(customBigger.FeedId, owner, ParticipantType.Owner),
            TestDataFactory.CreateParticipantEntity(customBigger.FeedId, TestDataFactory.CreateAddress(), ParticipantType.Member),
        };

        var customSmaller = new GroupFeed(
            TestDataFactory.CreateFeedId(),
            "Work",
            "custom",
            false,
            new BlockIndex(12),
            1,
            IsInnerCircle: false,
            OwnerPublicAddress: owner);
        customSmaller.Participants = new List<GroupFeedParticipantEntity>
        {
            TestDataFactory.CreateParticipantEntity(customSmaller.FeedId, owner, ParticipantType.Owner),
        };

        await context.GroupFeeds.AddRangeAsync(inner, customSmaller, customBigger);
        await context.SaveChangesAsync();

        // Act
        var circles = await repository.GetCirclesForOwnerAsync(owner);

        // Assert
        circles.Should().HaveCount(3);
        circles[0].IsInnerCircle.Should().BeTrue();
        circles[0].Name.Should().Be("Inner Circle");
        circles[1].Name.Should().Be("Friends");
        circles[2].Name.Should().Be("Work");
        circles[1].MemberCount.Should().BeGreaterThan(circles[2].MemberCount);
    }
}
