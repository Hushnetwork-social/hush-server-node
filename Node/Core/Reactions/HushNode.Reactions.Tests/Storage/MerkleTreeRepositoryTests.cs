using FluentAssertions;
using HushNode.Reactions.Storage;
using HushNode.Reactions.Tests.Fixtures;
using HushShared.Reactions.Model;
using Xunit;

namespace HushNode.Reactions.Tests.Storage;

/// <summary>
/// Tests for MerkleTreeRepository using in-memory database.
/// </summary>
public class MerkleTreeRepositoryTests : IClassFixture<InMemoryDbContextFixture>
{
    private readonly InMemoryDbContextFixture _fixture;

    public MerkleTreeRepositoryTests(InMemoryDbContextFixture fixture)
    {
        _fixture = fixture;
    }

    private MerkleTreeRepository CreateRepository(ReactionsDbContext context)
    {
        var repository = new MerkleTreeRepository();
        repository.SetContext(context);
        return repository;
    }

    [Fact]
    public async Task SaveRootAsync_NewRoot_ShouldPersist()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();
        var root = TestDataFactory.CreateMerkleRootHistory(feedId, 100);

        await repository.SaveRootAsync(root);
        await context.SaveChangesAsync();

        var recentRoots = await repository.GetRecentRootsAsync(feedId, 1);
        recentRoots.Should().HaveCount(1);
        recentRoots.First().MerkleRoot.Should().BeEquivalentTo(root.MerkleRoot);
        recentRoots.First().BlockHeight.Should().Be(100);
    }

    [Fact]
    public async Task GetRecentRootsAsync_NoRoots_ShouldReturnEmpty()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();

        var result = await repository.GetRecentRootsAsync(feedId, 3);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentRootsAsync_WithRoots_ShouldReturnRequested()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();

        // Add 5 roots
        for (int i = 0; i < 5; i++)
        {
            var root = TestDataFactory.CreateMerkleRootHistory(feedId, 100 + i);
            await repository.SaveRootAsync(root);
        }
        await context.SaveChangesAsync();

        var result = (await repository.GetRecentRootsAsync(feedId, 3)).ToList();

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetRecentRootsAsync_RequestMoreThanAvailable_ShouldReturnAll()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();

        await repository.SaveRootAsync(TestDataFactory.CreateMerkleRootHistory(feedId, 100));
        await repository.SaveRootAsync(TestDataFactory.CreateMerkleRootHistory(feedId, 101));
        await context.SaveChangesAsync();

        var result = await repository.GetRecentRootsAsync(feedId, 10);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetRecentRootsAsync_ShouldOnlyReturnForSpecificFeed()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId1 = TestDataFactory.CreateFeedId();
        var feedId2 = TestDataFactory.CreateFeedId();

        await repository.SaveRootAsync(TestDataFactory.CreateMerkleRootHistory(feedId1, 100));
        await repository.SaveRootAsync(TestDataFactory.CreateMerkleRootHistory(feedId1, 101));
        await repository.SaveRootAsync(TestDataFactory.CreateMerkleRootHistory(feedId2, 100));
        await context.SaveChangesAsync();

        var result1 = await repository.GetRecentRootsAsync(feedId1, 10);
        var result2 = await repository.GetRecentRootsAsync(feedId2, 10);

        result1.Should().HaveCount(2);
        result2.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetCommitmentsAsync_NoCommitments_ShouldReturnEmpty()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();

        var result = await repository.GetCommitmentsAsync(feedId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCommitmentsAsync_WithCommitments_ShouldReturnAll()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var commitmentRepo = new CommitmentRepository();
        commitmentRepo.SetContext(context);

        var feedId = TestDataFactory.CreateFeedId();

        // Add some commitments via commitment repository
        for (int i = 0; i < 3; i++)
        {
            var commitment = new FeedMemberCommitment(
                feedId,
                TestDataFactory.CreateCommitment(),
                DateTime.UtcNow.AddMinutes(i));
            await commitmentRepo.AddCommitmentAsync(commitment);
        }
        await context.SaveChangesAsync();

        var result = await repository.GetCommitmentsAsync(feedId);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetCommitmentCountAsync_NoCommitments_ShouldReturnZero()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();

        var result = await repository.GetCommitmentCountAsync(feedId);

        result.Should().Be(0);
    }

    [Fact]
    public async Task GetCommitmentCountAsync_WithCommitments_ShouldReturnCount()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);
        var commitmentRepo = new CommitmentRepository();
        commitmentRepo.SetContext(context);

        var feedId = TestDataFactory.CreateFeedId();

        // Add some commitments
        for (int i = 0; i < 5; i++)
        {
            var commitment = new FeedMemberCommitment(
                feedId,
                TestDataFactory.CreateCommitment(),
                DateTime.UtcNow.AddMinutes(i));
            await commitmentRepo.AddCommitmentAsync(commitment);
        }
        await context.SaveChangesAsync();

        var result = await repository.GetCommitmentCountAsync(feedId);

        result.Should().Be(5);
    }

    [Fact]
    public async Task IsRootValidAsync_RootInGracePeriod_ShouldReturnTrue()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();
        var root = TestDataFactory.CreateMerkleRootHistory(feedId, 100);

        await repository.SaveRootAsync(root);
        await context.SaveChangesAsync();

        var result = await repository.IsRootValidAsync(feedId, root.MerkleRoot, 3);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsRootValidAsync_RootOutsideGracePeriod_ShouldReturnFalse()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();
        var oldRoot = TestDataFactory.CreateMerkleRootHistory(feedId, 100);

        await repository.SaveRootAsync(oldRoot);

        // Add 3 more roots after the old one
        for (int i = 1; i <= 3; i++)
        {
            await repository.SaveRootAsync(TestDataFactory.CreateMerkleRootHistory(feedId, 100 + i));
        }
        await context.SaveChangesAsync();

        // Old root should no longer be valid with grace period of 2
        var result = await repository.IsRootValidAsync(feedId, oldRoot.MerkleRoot, 2);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GracePeriodScenario_ShouldWorkCorrectly()
    {
        // Simulates the grace period scenario where we accept proofs
        // against any of the last N merkle roots
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();

        // User gets membership proof at block 100
        var root100 = TestDataFactory.CreateMerkleRootHistory(feedId, 100);
        await repository.SaveRootAsync(root100);
        await context.SaveChangesAsync();

        // While user is creating proof, new blocks come in
        await repository.SaveRootAsync(TestDataFactory.CreateMerkleRootHistory(feedId, 101));
        await repository.SaveRootAsync(TestDataFactory.CreateMerkleRootHistory(feedId, 102));
        await context.SaveChangesAsync();

        // Grace period: accept proofs against recent 3 roots
        var recentRoots = (await repository.GetRecentRootsAsync(feedId, 3)).ToList();

        // Original root should still be in grace period
        recentRoots.Should().Contain(r => r.MerkleRoot.SequenceEqual(root100.MerkleRoot));
    }
}
