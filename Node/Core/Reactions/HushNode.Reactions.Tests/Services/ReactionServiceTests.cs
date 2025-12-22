using FluentAssertions;
using HushNode.Reactions.Crypto;
using HushNode.Reactions.Storage;
using HushNode.Reactions.Tests.Fixtures;
using HushNode.Reactions.ZK;
using HushShared.Feeds.Model;
using HushShared.Reactions.Model;
using Microsoft.Extensions.Logging;
using Moq;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushNode.Reactions.Tests.Services;

/// <summary>
/// Tests for ReactionService - reaction submission and tally management.
/// </summary>
public class ReactionServiceTests
{
    private readonly Mock<IUnitOfWorkProvider<ReactionsDbContext>> _unitOfWorkProviderMock;
    private readonly Mock<IReadOnlyUnitOfWork<ReactionsDbContext>> _readOnlyUnitOfWorkMock;
    private readonly Mock<IWritableUnitOfWork<ReactionsDbContext>> _writableUnitOfWorkMock;
    private readonly Mock<IReactionsRepository> _reactionsRepoMock;
    private readonly Mock<IZkVerifier> _zkVerifierMock;
    private readonly Mock<IMembershipService> _membershipServiceMock;
    private readonly Mock<IFeedInfoProvider> _feedInfoProviderMock;
    private readonly IBabyJubJub _curve;
    private readonly Mock<ILogger<ReactionService>> _loggerMock;
    private readonly ReactionService _service;

    public ReactionServiceTests()
    {
        _unitOfWorkProviderMock = new Mock<IUnitOfWorkProvider<ReactionsDbContext>>();
        _readOnlyUnitOfWorkMock = new Mock<IReadOnlyUnitOfWork<ReactionsDbContext>>();
        _writableUnitOfWorkMock = new Mock<IWritableUnitOfWork<ReactionsDbContext>>();
        _reactionsRepoMock = MockRepositories.CreateReactionsRepository();
        _zkVerifierMock = new Mock<IZkVerifier>();
        _membershipServiceMock = new Mock<IMembershipService>();
        _feedInfoProviderMock = new Mock<IFeedInfoProvider>();
        _curve = new BabyJubJubCurve();
        _loggerMock = new Mock<ILogger<ReactionService>>();

        // Setup unit of work
        _readOnlyUnitOfWorkMock.Setup(x => x.GetRepository<IReactionsRepository>())
            .Returns(_reactionsRepoMock.Object);
        _writableUnitOfWorkMock.Setup(x => x.GetRepository<IReactionsRepository>())
            .Returns(_reactionsRepoMock.Object);

        _unitOfWorkProviderMock.Setup(x => x.CreateReadOnly())
            .Returns(_readOnlyUnitOfWorkMock.Object);
        _unitOfWorkProviderMock.Setup(x => x.CreateWritable(It.IsAny<System.Data.IsolationLevel>()))
            .Returns(_writableUnitOfWorkMock.Object);

        _service = new ReactionService(
            _unitOfWorkProviderMock.Object,
            _zkVerifierMock.Object,
            _curve,
            _membershipServiceMock.Object,
            _feedInfoProviderMock.Object,
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

    [Fact]
    public async Task SubmitReactionAsync_InvalidCiphertextSize_ShouldReturnError()
    {
        var request = TestDataFactory.CreateSubmitRequest();
        request.CiphertextC1 = TestDataFactory.CreateCiphertextArray(3); // Wrong size

        var result = await _service.SubmitReactionAsync(request);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_CIPHERTEXT_SIZE");
    }

    [Fact]
    public async Task SubmitReactionAsync_FeedNotFound_ShouldReturnError()
    {
        var request = TestDataFactory.CreateSubmitRequest();

        _feedInfoProviderMock.Setup(x => x.GetFeedPublicKeyAsync(request.FeedId))
            .ReturnsAsync((HushShared.Reactions.Model.ECPoint?)null);

        var result = await _service.SubmitReactionAsync(request);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("FEED_NOT_FOUND");
    }

    [Fact]
    public async Task SubmitReactionAsync_MessageNotFound_ShouldReturnError()
    {
        var request = TestDataFactory.CreateSubmitRequest();

        _feedInfoProviderMock.Setup(x => x.GetFeedPublicKeyAsync(request.FeedId))
            .ReturnsAsync(TestDataFactory.CreateRandomPoint());

        _feedInfoProviderMock.Setup(x => x.GetAuthorCommitmentAsync(request.MessageId))
            .ReturnsAsync((byte[]?)null);

        var result = await _service.SubmitReactionAsync(request);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("MESSAGE_NOT_FOUND");
    }

    [Fact]
    public async Task SubmitReactionAsync_NoMerkleRoots_ShouldReturnError()
    {
        var request = TestDataFactory.CreateSubmitRequest();

        SetupValidFeedAndMessage(request);

        _membershipServiceMock.Setup(x => x.GetRecentRootsAsync(request.FeedId, It.IsAny<int>()))
            .ReturnsAsync(Enumerable.Empty<MerkleRootHistory>());

        var result = await _service.SubmitReactionAsync(request);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("NO_MERKLE_ROOTS");
    }

    [Fact]
    public async Task SubmitReactionAsync_InvalidProof_ShouldReturnError()
    {
        var request = TestDataFactory.CreateSubmitRequest();

        SetupValidFeedAndMessage(request);
        SetupMerkleRoots(request.FeedId);

        _zkVerifierMock.Setup(x => x.VerifyAsync(
                It.IsAny<byte[]>(),
                It.IsAny<PublicInputs>(),
                It.IsAny<string>()))
            .ReturnsAsync(new VerifyResult { Valid = false, Error = "INVALID_PROOF", Message = "Proof verification failed" });

        var result = await _service.SubmitReactionAsync(request);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_PROOF");
    }

    [Fact]
    public async Task SubmitReactionAsync_ValidNewReaction_ShouldSucceed()
    {
        var request = TestDataFactory.CreateSubmitRequest();

        SetupValidFeedAndMessage(request);
        SetupMerkleRoots(request.FeedId);
        SetupValidZkVerification();

        var result = await _service.SubmitReactionAsync(request);

        result.Success.Should().BeTrue();
        result.TransactionId.Should().NotBeNull();

        // Verify tally was saved
        _reactionsRepoMock.Verify(x => x.SaveTallyAsync(It.IsAny<MessageReactionTally>()), Times.Once);

        // Verify nullifier was saved
        _reactionsRepoMock.Verify(x => x.SaveNullifierAsync(It.IsAny<ReactionNullifier>()), Times.Once);

        // Verify transaction was saved
        _reactionsRepoMock.Verify(x => x.SaveTransactionAsync(It.IsAny<ReactionTransaction>()), Times.Once);
    }

    [Fact]
    public async Task SubmitReactionAsync_UpdateExistingReaction_ShouldUpdateTally()
    {
        var request = TestDataFactory.CreateSubmitRequest();
        var existingNullifier = TestDataFactory.CreateNullifierRecord(request.MessageId) with
        {
            Nullifier = request.Nullifier
        };
        var existingTally = TestDataFactory.CreateTallyWithVotes(request.FeedId, request.MessageId, 1);

        SetupValidFeedAndMessage(request);
        SetupMerkleRoots(request.FeedId);
        SetupValidZkVerification();

        _reactionsRepoMock.SetupExistingNullifier(request.Nullifier, existingNullifier);
        _reactionsRepoMock.SetupExistingTally(request.MessageId, existingTally);

        var result = await _service.SubmitReactionAsync(request);

        result.Success.Should().BeTrue();

        // Verify nullifier was updated (not created new)
        _reactionsRepoMock.Verify(x => x.UpdateNullifierAsync(It.IsAny<ReactionNullifier>()), Times.Once);
        _reactionsRepoMock.Verify(x => x.SaveNullifierAsync(It.IsAny<ReactionNullifier>()), Times.Never);
    }

    [Fact]
    public async Task SubmitReactionAsync_NewReaction_ShouldIncrementTotalCount()
    {
        var request = TestDataFactory.CreateSubmitRequest();
        var existingTally = TestDataFactory.CreateTallyWithVotes(request.FeedId, request.MessageId, 5);

        SetupValidFeedAndMessage(request);
        SetupMerkleRoots(request.FeedId);
        SetupValidZkVerification();

        _reactionsRepoMock.SetupExistingTally(request.MessageId, existingTally);

        MessageReactionTally? savedTally = null;
        _reactionsRepoMock.Setup(x => x.SaveTallyAsync(It.IsAny<MessageReactionTally>()))
            .Callback<MessageReactionTally>(t => savedTally = t)
            .Returns(Task.CompletedTask);

        await _service.SubmitReactionAsync(request);

        savedTally.Should().NotBeNull();
        savedTally!.TotalCount.Should().Be(6); // Incremented
    }

    [Fact]
    public async Task SubmitReactionAsync_UpdateReaction_ShouldNotIncrementTotalCount()
    {
        var request = TestDataFactory.CreateSubmitRequest();
        var existingNullifier = TestDataFactory.CreateNullifierRecord(request.MessageId) with
        {
            Nullifier = request.Nullifier
        };
        var existingTally = TestDataFactory.CreateTallyWithVotes(request.FeedId, request.MessageId, 5);

        SetupValidFeedAndMessage(request);
        SetupMerkleRoots(request.FeedId);
        SetupValidZkVerification();

        _reactionsRepoMock.SetupExistingNullifier(request.Nullifier, existingNullifier);
        _reactionsRepoMock.SetupExistingTally(request.MessageId, existingTally);

        MessageReactionTally? savedTally = null;
        _reactionsRepoMock.Setup(x => x.SaveTallyAsync(It.IsAny<MessageReactionTally>()))
            .Callback<MessageReactionTally>(t => savedTally = t)
            .Returns(Task.CompletedTask);

        await _service.SubmitReactionAsync(request);

        savedTally.Should().NotBeNull();
        savedTally!.TotalCount.Should().Be(5); // Not incremented for update
    }

    private void SetupValidFeedAndMessage(HushNode.Reactions.Storage.SubmitReactionRequest request)
    {
        _feedInfoProviderMock.Setup(x => x.GetFeedPublicKeyAsync(request.FeedId))
            .ReturnsAsync(TestDataFactory.CreateRandomPoint());

        _feedInfoProviderMock.Setup(x => x.GetAuthorCommitmentAsync(request.MessageId))
            .ReturnsAsync(TestDataFactory.CreateCommitment());
    }

    private void SetupMerkleRoots(FeedId feedId)
    {
        var roots = new[]
        {
            TestDataFactory.CreateMerkleRootHistory(feedId, 100),
            TestDataFactory.CreateMerkleRootHistory(feedId, 99),
            TestDataFactory.CreateMerkleRootHistory(feedId, 98)
        };

        _membershipServiceMock.Setup(x => x.GetRecentRootsAsync(feedId, It.IsAny<int>()))
            .ReturnsAsync(roots);
    }

    private void SetupValidZkVerification()
    {
        _zkVerifierMock.Setup(x => x.VerifyAsync(
                It.IsAny<byte[]>(),
                It.IsAny<PublicInputs>(),
                It.IsAny<string>()))
            .ReturnsAsync(new VerifyResult { Valid = true });
    }
}
