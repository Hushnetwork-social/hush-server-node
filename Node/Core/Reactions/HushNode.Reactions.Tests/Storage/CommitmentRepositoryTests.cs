using FluentAssertions;
using HushNode.Reactions.Storage;
using HushNode.Reactions.Tests.Fixtures;
using HushShared.Reactions.Model;
using Xunit;

namespace HushNode.Reactions.Tests.Storage;

/// <summary>
/// Tests for CommitmentRepository using in-memory database.
/// </summary>
public class CommitmentRepositoryTests : IClassFixture<InMemoryDbContextFixture>
{
    private readonly InMemoryDbContextFixture _fixture;

    public CommitmentRepositoryTests(InMemoryDbContextFixture fixture)
    {
        _fixture = fixture;
    }

    private CommitmentRepository CreateRepository(ReactionsDbContext context)
    {
        var repository = new CommitmentRepository();
        repository.SetContext(context);
        return repository;
    }

    [Fact]
    public async Task AddCommitmentAsync_NewCommitment_ShouldPersist()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();
        var commitment = TestDataFactory.CreateMemberCommitment(feedId);

        await repository.AddCommitmentAsync(commitment);
        await context.SaveChangesAsync();

        var isRegistered = await repository.IsCommitmentRegisteredAsync(feedId, commitment.UserCommitment);
        isRegistered.Should().BeTrue();
    }

    [Fact]
    public async Task IsCommitmentRegisteredAsync_NotRegistered_ShouldReturnFalse()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();
        var commitment = TestDataFactory.CreateCommitment();

        var result = await repository.IsCommitmentRegisteredAsync(feedId, commitment);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsCommitmentRegisteredAsync_DifferentFeed_ShouldReturnFalse()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId1 = TestDataFactory.CreateFeedId();
        var feedId2 = TestDataFactory.CreateFeedId();
        var commitment = TestDataFactory.CreateMemberCommitment(feedId1);

        await repository.AddCommitmentAsync(commitment);
        await context.SaveChangesAsync();

        var result = await repository.IsCommitmentRegisteredAsync(feedId2, commitment.UserCommitment);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetCommitmentsForFeedAsync_EmptyFeed_ShouldReturnEmpty()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();

        var result = await repository.GetCommitmentsForFeedAsync(feedId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCommitmentsForFeedAsync_WithCommitments_ShouldReturnAll()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();

        for (int i = 0; i < 5; i++)
        {
            var commitment = new FeedMemberCommitment(
                feedId,
                TestDataFactory.CreateCommitment(),
                DateTime.UtcNow.AddMinutes(i));
            await repository.AddCommitmentAsync(commitment);
        }
        await context.SaveChangesAsync();

        var result = await repository.GetCommitmentsForFeedAsync(feedId);

        result.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetCommitmentsForFeedAsync_ShouldOnlyReturnForSpecificFeed()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId1 = TestDataFactory.CreateFeedId();
        var feedId2 = TestDataFactory.CreateFeedId();

        await repository.AddCommitmentAsync(TestDataFactory.CreateMemberCommitment(feedId1));
        await repository.AddCommitmentAsync(TestDataFactory.CreateMemberCommitment(feedId1));
        await repository.AddCommitmentAsync(TestDataFactory.CreateMemberCommitment(feedId2));
        await context.SaveChangesAsync();

        var result1 = await repository.GetCommitmentsForFeedAsync(feedId1);
        var result2 = await repository.GetCommitmentsForFeedAsync(feedId2);

        result1.Should().HaveCount(2);
        result2.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetCommitmentIndexAsync_NotFound_ShouldReturnNegativeOne()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();
        var commitment = TestDataFactory.CreateCommitment();

        var result = await repository.GetCommitmentIndexAsync(feedId, commitment);

        result.Should().Be(-1);
    }

    [Fact]
    public async Task GetCommitmentIndexAsync_Found_ShouldReturnCorrectIndex()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();
        var baseTime = DateTime.UtcNow;

        // Add 3 commitments in order
        var commitment1 = new FeedMemberCommitment(feedId, TestDataFactory.CreateCommitment(), baseTime);
        var commitment2 = new FeedMemberCommitment(feedId, TestDataFactory.CreateCommitment(), baseTime.AddMinutes(1));
        var commitment3 = new FeedMemberCommitment(feedId, TestDataFactory.CreateCommitment(), baseTime.AddMinutes(2));

        await repository.AddCommitmentAsync(commitment1);
        await repository.AddCommitmentAsync(commitment2);
        await repository.AddCommitmentAsync(commitment3);
        await context.SaveChangesAsync();

        var index1 = await repository.GetCommitmentIndexAsync(feedId, commitment1.UserCommitment);
        var index2 = await repository.GetCommitmentIndexAsync(feedId, commitment2.UserCommitment);
        var index3 = await repository.GetCommitmentIndexAsync(feedId, commitment3.UserCommitment);

        index1.Should().Be(0);
        index2.Should().Be(1);
        index3.Should().Be(2);
    }

    [Fact]
    public async Task GetCommitmentsForFeedAsync_ShouldOrderByRegisteredAt()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();
        var baseTime = DateTime.UtcNow;

        // Add in reverse order
        for (int i = 4; i >= 0; i--)
        {
            var commitment = new FeedMemberCommitment(
                feedId,
                TestDataFactory.CreateCommitment(),
                baseTime.AddMinutes(i));
            await repository.AddCommitmentAsync(commitment);
        }
        await context.SaveChangesAsync();

        var result = (await repository.GetCommitmentsForFeedAsync(feedId)).ToList();

        // Should be ordered by registration time
        for (int i = 0; i < result.Count - 1; i++)
        {
            result[i].RegisteredAt.Should().BeBefore(result[i + 1].RegisteredAt);
        }
    }
}
