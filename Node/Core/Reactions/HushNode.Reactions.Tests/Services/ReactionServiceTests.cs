using FluentAssertions;
using HushNode.Reactions.Storage;
using HushNode.Reactions.Tests.Fixtures;
using HushShared.Feeds.Model;
using HushShared.Reactions.Model;
using Microsoft.Extensions.Logging;
using Moq;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushNode.Reactions.Tests.Services;

/// <summary>
/// Tests for ReactionService - reaction query operations.
///
/// NOTE: Reaction submission tests are in ReactionTransactionHandlerTests
/// since submission is now handled via blockchain transactions.
/// </summary>
public class ReactionServiceTests
{
    private readonly Mock<IUnitOfWorkProvider<ReactionsDbContext>> _unitOfWorkProviderMock;
    private readonly Mock<IReadOnlyUnitOfWork<ReactionsDbContext>> _readOnlyUnitOfWorkMock;
    private readonly Mock<IReactionsRepository> _reactionsRepoMock;
    private readonly Mock<ILogger<ReactionService>> _loggerMock;
    private readonly ReactionService _service;

    public ReactionServiceTests()
    {
        _unitOfWorkProviderMock = new Mock<IUnitOfWorkProvider<ReactionsDbContext>>();
        _readOnlyUnitOfWorkMock = new Mock<IReadOnlyUnitOfWork<ReactionsDbContext>>();
        _reactionsRepoMock = MockRepositories.CreateReactionsRepository();
        _loggerMock = new Mock<ILogger<ReactionService>>();

        // Setup unit of work
        _readOnlyUnitOfWorkMock.Setup(x => x.GetRepository<IReactionsRepository>())
            .Returns(_reactionsRepoMock.Object);

        _unitOfWorkProviderMock.Setup(x => x.CreateReadOnly())
            .Returns(_readOnlyUnitOfWorkMock.Object);

        _service = new ReactionService(
            _unitOfWorkProviderMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task NullifierExistsAsync_NotExists_ShouldReturnFalse()
    {
        var nullifier = TestDataFactory.CreateNullifier();

        var result = await _service.NullifierExistsAsync(nullifier);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task NullifierExistsAsync_Exists_ShouldReturnTrue()
    {
        var nullifier = TestDataFactory.CreateNullifier();
        var messageId = TestDataFactory.CreateMessageId();
        var record = TestDataFactory.CreateNullifierRecord(messageId);

        _reactionsRepoMock.SetupExistingNullifier(nullifier, record with { Nullifier = nullifier });

        var result = await _service.NullifierExistsAsync(nullifier);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetReactionBackupAsync_NotExists_ShouldReturnNull()
    {
        var nullifier = TestDataFactory.CreateNullifier();

        var result = await _service.GetReactionBackupAsync(nullifier);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetReactionBackupAsync_Exists_ShouldReturnBackup()
    {
        var nullifier = TestDataFactory.CreateNullifier();
        var messageId = TestDataFactory.CreateMessageId();
        var backup = TestDataFactory.CreateCommitment();
        var record = TestDataFactory.CreateNullifierRecord(messageId) with
        {
            Nullifier = nullifier,
            EncryptedEmojiBackup = backup
        };

        _reactionsRepoMock.SetupExistingNullifier(nullifier, record);

        var result = await _service.GetReactionBackupAsync(nullifier);

        result.Should().BeEquivalentTo(backup);
    }

    [Fact]
    public async Task GetTalliesAsync_NoTallies_ShouldReturnEmpty()
    {
        var feedId = TestDataFactory.CreateFeedId();
        var messageIds = new[] { TestDataFactory.CreateMessageId() };

        var result = await _service.GetTalliesAsync(feedId, messageIds);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTalliesAsync_WithTallies_ShouldReturnThem()
    {
        var feedId = TestDataFactory.CreateFeedId();
        var messageId = TestDataFactory.CreateMessageId();
        var tally = TestDataFactory.CreateTallyWithVotes(feedId, messageId, 5);

        _reactionsRepoMock.Setup(x => x.GetTalliesForMessagesAsync(It.IsAny<IEnumerable<FeedMessageId>>()))
            .ReturnsAsync(new[] { tally });

        var result = await _service.GetTalliesAsync(feedId, new[] { messageId });

        result.Should().HaveCount(1);
        result.First().TotalCount.Should().Be(5);
    }
}
