using FluentAssertions;
using HushNode.Reactions.Storage;
using HushNode.Reactions.Tests.Fixtures;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using HushShared.Reactions.Model;
using Xunit;

namespace HushNode.Reactions.Tests.Storage;

/// <summary>
/// Tests for ReactionsRepository using in-memory database.
/// </summary>
public class ReactionsRepositoryTests : IClassFixture<InMemoryDbContextFixture>
{
    private readonly InMemoryDbContextFixture _fixture;

    public ReactionsRepositoryTests(InMemoryDbContextFixture fixture)
    {
        _fixture = fixture;
    }

    private ReactionsRepository CreateRepository(ReactionsDbContext context)
    {
        var repository = new ReactionsRepository();
        repository.SetContext(context);
        return repository;
    }

    [Fact]
    public async Task SaveTallyAsync_NewTally_ShouldPersist()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();
        var messageId = TestDataFactory.CreateMessageId();
        var tally = TestDataFactory.CreateEmptyTally(feedId, messageId);

        await repository.SaveTallyAsync(tally);
        await context.SaveChangesAsync();

        var retrieved = await repository.GetTallyAsync(messageId);
        retrieved.Should().NotBeNull();
        retrieved!.MessageId.Should().Be(messageId);
        retrieved.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task SaveTallyAsync_UpdateExisting_ShouldUpdate()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();
        var messageId = TestDataFactory.CreateMessageId();
        var tally = TestDataFactory.CreateEmptyTally(feedId, messageId);

        await repository.SaveTallyAsync(tally);
        await context.SaveChangesAsync();

        var updatedTally = tally with { TotalCount = 5 };
        await repository.SaveTallyAsync(updatedTally);
        await context.SaveChangesAsync();

        var retrieved = await repository.GetTallyAsync(messageId);
        retrieved!.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task GetTallyAsync_NotExists_ShouldReturnNull()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var messageId = TestDataFactory.CreateMessageId();

        var result = await repository.GetTallyAsync(messageId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetTalliesForMessagesAsync_ShouldReturnMatchingTallies()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();
        var messageId1 = TestDataFactory.CreateMessageId();
        var messageId2 = TestDataFactory.CreateMessageId();
        var messageId3 = TestDataFactory.CreateMessageId();

        await repository.SaveTallyAsync(TestDataFactory.CreateTallyWithVotes(feedId, messageId1, 1));
        await repository.SaveTallyAsync(TestDataFactory.CreateTallyWithVotes(feedId, messageId2, 2));
        await context.SaveChangesAsync();

        var result = await repository.GetTalliesForMessagesAsync(new[] { messageId1, messageId2, messageId3 });

        result.Should().HaveCount(2);
        result.Should().Contain(t => t.MessageId == messageId1);
        result.Should().Contain(t => t.MessageId == messageId2);
    }

    [Fact]
    public async Task SaveNullifierAsync_NewNullifier_ShouldPersist()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var messageId = TestDataFactory.CreateMessageId();
        var nullifier = TestDataFactory.CreateNullifierRecord(messageId);

        await repository.SaveNullifierAsync(nullifier);
        await context.SaveChangesAsync();

        var exists = await repository.NullifierExistsAsync(nullifier.Nullifier);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task NullifierExistsAsync_NotExists_ShouldReturnFalse()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var nullifier = TestDataFactory.CreateNullifier();

        var result = await repository.NullifierExistsAsync(nullifier);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetNullifierAsync_Exists_ShouldReturnRecord()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var messageId = TestDataFactory.CreateMessageId();
        var nullifier = TestDataFactory.CreateNullifierRecord(messageId);

        await repository.SaveNullifierAsync(nullifier);
        await context.SaveChangesAsync();

        var result = await repository.GetNullifierAsync(nullifier.Nullifier);

        result.Should().NotBeNull();
        result!.MessageId.Should().Be(messageId);
    }

    [Fact]
    public async Task GetNullifierWithBackupAsync_ShouldReturnBackup()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var messageId = TestDataFactory.CreateMessageId();
        var backup = TestDataFactory.CreateCommitment();
        var nullifier = TestDataFactory.CreateNullifierRecord(messageId) with
        {
            EncryptedEmojiBackup = backup
        };

        await repository.SaveNullifierAsync(nullifier);
        await context.SaveChangesAsync();

        var result = await repository.GetNullifierWithBackupAsync(nullifier.Nullifier);

        result.Should().NotBeNull();
        result!.EncryptedEmojiBackup.Should().BeEquivalentTo(backup);
    }

    [Fact]
    public async Task UpdateNullifierAsync_ShouldUpdateRecord()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var messageId = TestDataFactory.CreateMessageId();
        var nullifier = TestDataFactory.CreateNullifierRecord(messageId);

        await repository.SaveNullifierAsync(nullifier);
        await context.SaveChangesAsync();

        var newBackup = TestDataFactory.CreateCommitment();
        var updated = nullifier with
        {
            EncryptedEmojiBackup = newBackup,
            UpdatedAt = DateTime.UtcNow.AddMinutes(1)
        };

        await repository.UpdateNullifierAsync(updated);
        await context.SaveChangesAsync();

        var result = await repository.GetNullifierWithBackupAsync(nullifier.Nullifier);
        result!.EncryptedEmojiBackup.Should().BeEquivalentTo(newBackup);
    }

    [Fact]
    public async Task SaveTransactionAsync_ShouldPersist()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();
        var messageId = TestDataFactory.CreateMessageId();
        var transaction = new ReactionTransaction(
            Id: Guid.NewGuid(),
            BlockHeight: new BlockIndex(100),
            FeedId: feedId,
            MessageId: messageId,
            Nullifier: TestDataFactory.CreateNullifier(),
            CiphertextC1X: TestDataFactory.CreateByteArrays(),
            CiphertextC1Y: TestDataFactory.CreateByteArrays(),
            CiphertextC2X: TestDataFactory.CreateByteArrays(),
            CiphertextC2Y: TestDataFactory.CreateByteArrays(),
            ZkProof: TestDataFactory.CreateZkProof(),
            CircuitVersion: "omega-v1.0.0",
            CreatedAt: DateTime.UtcNow);

        await repository.SaveTransactionAsync(transaction);
        await context.SaveChangesAsync();

        var result = await repository.GetTransactionsFromBlockAsync(new BlockIndex(100));
        result.Should().ContainSingle();
        result.First().Id.Should().Be(transaction.Id);
    }

    [Fact]
    public async Task GetTransactionsFromBlockAsync_ShouldFilterByBlock()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();

        // Create transactions in different blocks
        for (int i = 1; i <= 5; i++)
        {
            var transaction = new ReactionTransaction(
                Id: Guid.NewGuid(),
                BlockHeight: new BlockIndex(100 + i),
                FeedId: feedId,
                MessageId: TestDataFactory.CreateMessageId(),
                Nullifier: TestDataFactory.CreateNullifier(),
                CiphertextC1X: TestDataFactory.CreateByteArrays(),
                CiphertextC1Y: TestDataFactory.CreateByteArrays(),
                CiphertextC2X: TestDataFactory.CreateByteArrays(),
                CiphertextC2Y: TestDataFactory.CreateByteArrays(),
                ZkProof: TestDataFactory.CreateZkProof(),
                CircuitVersion: "omega-v1.0.0",
                CreatedAt: DateTime.UtcNow);

            await repository.SaveTransactionAsync(transaction);
        }
        await context.SaveChangesAsync();

        // Get transactions from block 103 onward
        var result = await repository.GetTransactionsFromBlockAsync(new BlockIndex(103));
        result.Should().HaveCount(3); // blocks 103, 104, 105
    }

    // ============= Reaction Sync Tests (Protocol Omega Phase 9) =============

    [Fact]
    public async Task GetTalliesForFeedsAsync_ShouldReturnTalliesForMatchingFeeds()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId1 = TestDataFactory.CreateFeedId();
        var feedId2 = TestDataFactory.CreateFeedId();
        var feedId3 = TestDataFactory.CreateFeedId(); // Not in query

        var messageId1 = TestDataFactory.CreateMessageId();
        var messageId2 = TestDataFactory.CreateMessageId();
        var messageId3 = TestDataFactory.CreateMessageId();

        // Create tallies with different versions
        await repository.SaveTallyAsync(TestDataFactory.CreateTallyWithVotes(feedId1, messageId1, 5, version: 10));
        await repository.SaveTallyAsync(TestDataFactory.CreateTallyWithVotes(feedId2, messageId2, 3, version: 20));
        await repository.SaveTallyAsync(TestDataFactory.CreateTallyWithVotes(feedId3, messageId3, 7, version: 30));
        await context.SaveChangesAsync();

        // Query for tallies in feedId1 and feedId2 with sinceVersion 0 (get all)
        var result = await repository.GetTalliesForFeedsAsync(new[] { feedId1, feedId2 }, 0);

        result.Should().HaveCount(2);
        result.Should().Contain(t => t.FeedId == feedId1 && t.Version == 10);
        result.Should().Contain(t => t.FeedId == feedId2 && t.Version == 20);
        result.Should().NotContain(t => t.FeedId == feedId3);
    }

    [Fact]
    public async Task GetTalliesForFeedsAsync_ShouldFilterByVersion()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();
        var messageId1 = TestDataFactory.CreateMessageId();
        var messageId2 = TestDataFactory.CreateMessageId();
        var messageId3 = TestDataFactory.CreateMessageId();

        // Create tallies with different versions
        await repository.SaveTallyAsync(TestDataFactory.CreateTallyWithVotes(feedId, messageId1, 1, version: 5));
        await repository.SaveTallyAsync(TestDataFactory.CreateTallyWithVotes(feedId, messageId2, 2, version: 10));
        await repository.SaveTallyAsync(TestDataFactory.CreateTallyWithVotes(feedId, messageId3, 3, version: 15));
        await context.SaveChangesAsync();

        // Query with sinceVersion 7 - should only return versions > 7
        var result = await repository.GetTalliesForFeedsAsync(new[] { feedId }, 7);

        result.Should().HaveCount(2);
        result.Should().Contain(t => t.Version == 10);
        result.Should().Contain(t => t.Version == 15);
        result.Should().NotContain(t => t.Version == 5);
    }

    [Fact]
    public async Task GetTalliesForFeedsAsync_ShouldReturnEmptyForNoMatches()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();
        var otherFeedId = TestDataFactory.CreateFeedId();
        var messageId = TestDataFactory.CreateMessageId();

        // Create tally for a different feed
        await repository.SaveTallyAsync(TestDataFactory.CreateTallyWithVotes(otherFeedId, messageId, 5, version: 10));
        await context.SaveChangesAsync();

        // Query for feedId that has no tallies
        var result = await repository.GetTalliesForFeedsAsync(new[] { feedId }, 0);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTalliesForFeedsAsync_ShouldReturnEmptyForEmptyFeedList()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();
        var messageId = TestDataFactory.CreateMessageId();

        // Create a tally
        await repository.SaveTallyAsync(TestDataFactory.CreateTallyWithVotes(feedId, messageId, 5, version: 10));
        await context.SaveChangesAsync();

        // Query with empty feed list
        var result = await repository.GetTalliesForFeedsAsync(new List<FeedId>(), 0);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTalliesForFeedsAsync_ShouldReturnAllWhenVersionIsZero()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();

        // Create multiple tallies
        for (int i = 1; i <= 5; i++)
        {
            var messageId = TestDataFactory.CreateMessageId();
            await repository.SaveTallyAsync(TestDataFactory.CreateTallyWithVotes(feedId, messageId, i, version: i));
        }
        await context.SaveChangesAsync();

        // Query with version 0 - should return all
        var result = await repository.GetTalliesForFeedsAsync(new[] { feedId }, 0);

        result.Should().HaveCount(5);
    }

    [Fact]
    public async Task SaveTallyAsync_ShouldIncrementVersion()
    {
        using var context = _fixture.CreateContext();
        var repository = CreateRepository(context);

        var feedId = TestDataFactory.CreateFeedId();
        var messageId = TestDataFactory.CreateMessageId();

        // Create initial tally with version 1
        var tally = TestDataFactory.CreateEmptyTally(feedId, messageId, version: 1);
        await repository.SaveTallyAsync(tally);
        await context.SaveChangesAsync();

        // Update tally with version 2
        var updatedTally = tally with { TotalCount = 5, Version = 2 };
        await repository.SaveTallyAsync(updatedTally);
        await context.SaveChangesAsync();

        var retrieved = await repository.GetTallyAsync(messageId);
        retrieved!.Version.Should().Be(2);
        retrieved.TotalCount.Should().Be(5);
    }
}
