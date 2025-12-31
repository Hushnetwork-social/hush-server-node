using FluentAssertions;
using HushNode.Caching;
using HushNode.Credentials;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using Moq;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for NewGroupFeedMessageContentHandler - validates KeyGeneration and member status.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class NewGroupFeedMessageContentHandlerTests
{
    private const int GracePeriodBlocks = 5;

    #region KeyGeneration Validation Tests

    [Fact]
    public void ValidateAndSign_CurrentKeyGeneration_Success()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var senderAddress = TestDataFactory.CreateAddress();

        SetupGroupFeed(mocker, feedId, currentKeyGeneration: 5);
        SetupMemberCanSend(mocker, feedId, senderAddress, canSend: true);
        SetupBlockchainCache(mocker, currentBlockIndex: 100);
        SetupCredentials(mocker);

        var sut = mocker.CreateInstance<NewGroupFeedMessageContentHandler>();
        var transaction = CreateSignedTransaction(feedId, senderAddress, keyGeneration: 5);

        // Act
        var result = sut.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_PreviousKeyGeneration_WithinGracePeriod_Success()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var senderAddress = TestDataFactory.CreateAddress();

        SetupGroupFeed(mocker, feedId, currentKeyGeneration: 5);
        SetupMemberCanSend(mocker, feedId, senderAddress, canSend: true);
        SetupBlockchainCache(mocker, currentBlockIndex: 103); // 3 blocks since key rotation
        SetupKeyGenerationEntity(mocker, feedId, keyGeneration: 5, validFromBlock: 100);
        SetupCredentials(mocker);

        var sut = mocker.CreateInstance<NewGroupFeedMessageContentHandler>();
        var transaction = CreateSignedTransaction(feedId, senderAddress, keyGeneration: 4);

        // Act
        var result = sut.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull("within grace period of 5 blocks");
    }

    [Fact]
    public void ValidateAndSign_PreviousKeyGeneration_AtGracePeriodBoundary_Success()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var senderAddress = TestDataFactory.CreateAddress();

        SetupGroupFeed(mocker, feedId, currentKeyGeneration: 5);
        SetupMemberCanSend(mocker, feedId, senderAddress, canSend: true);
        SetupBlockchainCache(mocker, currentBlockIndex: 104); // 4 blocks since key rotation (last valid)
        SetupKeyGenerationEntity(mocker, feedId, keyGeneration: 5, validFromBlock: 100);
        SetupCredentials(mocker);

        var sut = mocker.CreateInstance<NewGroupFeedMessageContentHandler>();
        var transaction = CreateSignedTransaction(feedId, senderAddress, keyGeneration: 4);

        // Act
        var result = sut.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull("block 104 is still within 5-block grace period (100, 101, 102, 103, 104)");
    }

    [Fact]
    public void ValidateAndSign_PreviousKeyGeneration_OutsideGracePeriod_ReturnsNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var senderAddress = TestDataFactory.CreateAddress();

        SetupGroupFeed(mocker, feedId, currentKeyGeneration: 5);
        SetupMemberCanSend(mocker, feedId, senderAddress, canSend: true);
        SetupBlockchainCache(mocker, currentBlockIndex: 110); // 10 blocks since key rotation
        SetupKeyGenerationEntity(mocker, feedId, keyGeneration: 5, validFromBlock: 100);
        SetupCredentials(mocker);

        var sut = mocker.CreateInstance<NewGroupFeedMessageContentHandler>();
        var transaction = CreateSignedTransaction(feedId, senderAddress, keyGeneration: 4);

        // Act
        var result = sut.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull("grace period of 5 blocks has expired");
    }

    [Fact]
    public void ValidateAndSign_VeryOldKeyGeneration_ReturnsNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var senderAddress = TestDataFactory.CreateAddress();

        SetupGroupFeed(mocker, feedId, currentKeyGeneration: 5);
        SetupMemberCanSend(mocker, feedId, senderAddress, canSend: true);
        SetupBlockchainCache(mocker, currentBlockIndex: 100);
        SetupCredentials(mocker);

        var sut = mocker.CreateInstance<NewGroupFeedMessageContentHandler>();
        var transaction = CreateSignedTransaction(feedId, senderAddress, keyGeneration: 3); // N-2

        // Act
        var result = sut.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull("KeyGeneration 3 is too old (current is 5, N-2 is not allowed)");
    }

    [Fact]
    public void ValidateAndSign_FutureKeyGeneration_ReturnsNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var senderAddress = TestDataFactory.CreateAddress();

        SetupGroupFeed(mocker, feedId, currentKeyGeneration: 5);
        SetupMemberCanSend(mocker, feedId, senderAddress, canSend: true);
        SetupBlockchainCache(mocker, currentBlockIndex: 100);
        SetupCredentials(mocker);

        var sut = mocker.CreateInstance<NewGroupFeedMessageContentHandler>();
        var transaction = CreateSignedTransaction(feedId, senderAddress, keyGeneration: 6);

        // Act
        var result = sut.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull("future KeyGeneration is not allowed");
    }

    #endregion

    #region Member Status Validation Tests

    [Fact]
    public void ValidateAndSign_ActiveMember_Success()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var senderAddress = TestDataFactory.CreateAddress();

        SetupGroupFeed(mocker, feedId, currentKeyGeneration: 1);
        SetupMemberCanSend(mocker, feedId, senderAddress, canSend: true);
        SetupBlockchainCache(mocker, currentBlockIndex: 100);
        SetupCredentials(mocker);

        var sut = mocker.CreateInstance<NewGroupFeedMessageContentHandler>();
        var transaction = CreateSignedTransaction(feedId, senderAddress, keyGeneration: 1);

        // Act
        var result = sut.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_BlockedMember_ReturnsNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var senderAddress = TestDataFactory.CreateAddress();

        SetupGroupFeed(mocker, feedId, currentKeyGeneration: 1);
        SetupMemberCanSend(mocker, feedId, senderAddress, canSend: false); // Blocked cannot send
        SetupBlockchainCache(mocker, currentBlockIndex: 100);
        SetupCredentials(mocker);

        var sut = mocker.CreateInstance<NewGroupFeedMessageContentHandler>();
        var transaction = CreateSignedTransaction(feedId, senderAddress, keyGeneration: 1);

        // Act
        var result = sut.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull("blocked members cannot send messages");
    }

    [Fact]
    public void ValidateAndSign_BannedMember_ReturnsNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var senderAddress = TestDataFactory.CreateAddress();

        SetupGroupFeed(mocker, feedId, currentKeyGeneration: 1);
        SetupMemberCanSend(mocker, feedId, senderAddress, canSend: false); // Banned cannot send
        SetupBlockchainCache(mocker, currentBlockIndex: 100);
        SetupCredentials(mocker);

        var sut = mocker.CreateInstance<NewGroupFeedMessageContentHandler>();
        var transaction = CreateSignedTransaction(feedId, senderAddress, keyGeneration: 1);

        // Act
        var result = sut.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull("banned members cannot send messages");
    }

    [Fact]
    public void ValidateAndSign_NonMember_ReturnsNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var senderAddress = TestDataFactory.CreateAddress();

        SetupGroupFeed(mocker, feedId, currentKeyGeneration: 1);
        SetupMemberCanSend(mocker, feedId, senderAddress, canSend: false); // Not a member
        SetupBlockchainCache(mocker, currentBlockIndex: 100);
        SetupCredentials(mocker);

        var sut = mocker.CreateInstance<NewGroupFeedMessageContentHandler>();
        var transaction = CreateSignedTransaction(feedId, senderAddress, keyGeneration: 1);

        // Act
        var result = sut.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull("non-members cannot send messages");
    }

    #endregion

    #region AuthorCommitment Validation Tests (Protocol Omega)

    [Fact]
    public void ValidateAndSign_ValidAuthorCommitment_Success()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var senderAddress = TestDataFactory.CreateAddress();

        SetupGroupFeed(mocker, feedId, currentKeyGeneration: 1);
        SetupMemberCanSend(mocker, feedId, senderAddress, canSend: true);
        SetupBlockchainCache(mocker, currentBlockIndex: 100);
        SetupCredentials(mocker);

        var sut = mocker.CreateInstance<NewGroupFeedMessageContentHandler>();
        var authorCommitment = new byte[32]; // Valid 32-byte commitment
        new Random(42).NextBytes(authorCommitment);
        var transaction = CreateSignedTransactionWithCommitment(feedId, senderAddress, keyGeneration: 1, authorCommitment);

        // Act
        var result = sut.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull("valid 32-byte AuthorCommitment should pass");
    }

    [Fact]
    public void ValidateAndSign_NullAuthorCommitment_Success()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var senderAddress = TestDataFactory.CreateAddress();

        SetupGroupFeed(mocker, feedId, currentKeyGeneration: 1);
        SetupMemberCanSend(mocker, feedId, senderAddress, canSend: true);
        SetupBlockchainCache(mocker, currentBlockIndex: 100);
        SetupCredentials(mocker);

        var sut = mocker.CreateInstance<NewGroupFeedMessageContentHandler>();
        var transaction = CreateSignedTransactionWithCommitment(feedId, senderAddress, keyGeneration: 1, authorCommitment: null);

        // Act
        var result = sut.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull("null AuthorCommitment is optional and should pass");
    }

    [Fact]
    public void ValidateAndSign_TooShortAuthorCommitment_ReturnsNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var senderAddress = TestDataFactory.CreateAddress();

        SetupGroupFeed(mocker, feedId, currentKeyGeneration: 1);
        SetupMemberCanSend(mocker, feedId, senderAddress, canSend: true);
        SetupBlockchainCache(mocker, currentBlockIndex: 100);
        SetupCredentials(mocker);

        var sut = mocker.CreateInstance<NewGroupFeedMessageContentHandler>();
        var authorCommitment = new byte[16]; // Too short - should be 32 bytes
        var transaction = CreateSignedTransactionWithCommitment(feedId, senderAddress, keyGeneration: 1, authorCommitment);

        // Act
        var result = sut.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull("AuthorCommitment must be exactly 32 bytes");
    }

    [Fact]
    public void ValidateAndSign_TooLongAuthorCommitment_ReturnsNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var senderAddress = TestDataFactory.CreateAddress();

        SetupGroupFeed(mocker, feedId, currentKeyGeneration: 1);
        SetupMemberCanSend(mocker, feedId, senderAddress, canSend: true);
        SetupBlockchainCache(mocker, currentBlockIndex: 100);
        SetupCredentials(mocker);

        var sut = mocker.CreateInstance<NewGroupFeedMessageContentHandler>();
        var authorCommitment = new byte[64]; // Too long - should be 32 bytes
        var transaction = CreateSignedTransactionWithCommitment(feedId, senderAddress, keyGeneration: 1, authorCommitment);

        // Act
        var result = sut.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull("AuthorCommitment must be exactly 32 bytes");
    }

    [Fact]
    public void ValidateAndSign_EmptyAuthorCommitment_ReturnsNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var senderAddress = TestDataFactory.CreateAddress();

        SetupGroupFeed(mocker, feedId, currentKeyGeneration: 1);
        SetupMemberCanSend(mocker, feedId, senderAddress, canSend: true);
        SetupBlockchainCache(mocker, currentBlockIndex: 100);
        SetupCredentials(mocker);

        var sut = mocker.CreateInstance<NewGroupFeedMessageContentHandler>();
        var authorCommitment = Array.Empty<byte>(); // Empty array
        var transaction = CreateSignedTransactionWithCommitment(feedId, senderAddress, keyGeneration: 1, authorCommitment);

        // Act
        var result = sut.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull("empty AuthorCommitment is invalid - must be 32 bytes or null");
    }

    #endregion

    #region Group State Validation Tests

    [Fact]
    public void ValidateAndSign_DeletedGroup_ReturnsNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var senderAddress = TestDataFactory.CreateAddress();

        SetupGroupFeed(mocker, feedId, currentKeyGeneration: 1, isDeleted: true);
        SetupBlockchainCache(mocker, currentBlockIndex: 100);
        SetupCredentials(mocker);

        var sut = mocker.CreateInstance<NewGroupFeedMessageContentHandler>();
        var transaction = CreateSignedTransaction(feedId, senderAddress, keyGeneration: 1);

        // Act
        var result = sut.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull("deleted groups cannot receive messages");
    }

    [Fact]
    public void ValidateAndSign_NonExistentGroup_ReturnsNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var senderAddress = TestDataFactory.CreateAddress();

        // Group does not exist
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetGroupFeedAsync(feedId))
            .ReturnsAsync((GroupFeed?)null);

        SetupBlockchainCache(mocker, currentBlockIndex: 100);
        SetupCredentials(mocker);

        var sut = mocker.CreateInstance<NewGroupFeedMessageContentHandler>();
        var transaction = CreateSignedTransaction(feedId, senderAddress, keyGeneration: 1);

        // Act
        var result = sut.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull("non-existent groups cannot receive messages");
    }

    [Fact]
    public void ValidateAndSign_EmptySenderAddress_ReturnsNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();

        var sut = mocker.CreateInstance<NewGroupFeedMessageContentHandler>();
        var transaction = CreateSignedTransactionWithEmptySender(feedId, keyGeneration: 1);

        // Act
        var result = sut.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull("sender address is required");
    }

    #endregion

    #region Helper Methods

    private static void SetupGroupFeed(AutoMocker mocker, FeedId feedId, int currentKeyGeneration, bool isDeleted = false)
    {
        var groupFeed = new GroupFeed(
            feedId,
            "Test Group",
            "Description",
            IsPublic: true,
            new BlockIndex(1),
            currentKeyGeneration)
        {
            IsDeleted = isDeleted
        };

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetGroupFeedAsync(feedId))
            .ReturnsAsync(groupFeed);
    }

    private static void SetupMemberCanSend(AutoMocker mocker, FeedId feedId, string publicAddress, bool canSend)
    {
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CanMemberSendMessagesAsync(feedId, publicAddress))
            .ReturnsAsync(canSend);
    }

    private static void SetupKeyGenerationEntity(AutoMocker mocker, FeedId feedId, int keyGeneration, long validFromBlock)
    {
        var keyGenEntity = new GroupFeedKeyGenerationEntity(
            feedId,
            keyGeneration,
            new BlockIndex(validFromBlock),
            RotationTrigger.Join);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetKeyGenerationByNumberAsync(feedId, keyGeneration))
            .ReturnsAsync(keyGenEntity);
    }

    private static void SetupBlockchainCache(AutoMocker mocker, long currentBlockIndex)
    {
        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(currentBlockIndex));
    }

    private static void SetupCredentials(AutoMocker mocker)
    {
        var credentials = new CredentialsProfile
        {
            ProfileName = "TestBlockProducer",
            PublicSigningAddress = "validator-public-address",
            PrivateSigningKey = Convert.ToHexString(new byte[32]),
            PublicEncryptAddress = "validator-encrypt-address",
            PrivateEncryptKey = Convert.ToHexString(new byte[32]),
            IsPublic = false
        };

        mocker.GetMock<ICredentialsProvider>()
            .Setup(x => x.GetCredentials())
            .Returns(credentials);
    }

    private static SignedTransaction<NewGroupFeedMessagePayload> CreateSignedTransaction(
        FeedId feedId, string senderAddress, int keyGeneration)
    {
        var payload = new NewGroupFeedMessagePayload(
            new FeedMessageId(Guid.NewGuid()),
            feedId,
            "encrypted-content",
            keyGeneration);

        var signature = new SignatureInfo(senderAddress, "signature");

        var unsignedTx = new UnsignedTransaction<NewGroupFeedMessagePayload>(
            new TransactionId(Guid.NewGuid()),
            NewGroupFeedMessagePayloadHandler.NewGroupFeedMessagePayloadKind,
            Timestamp.Current,
            payload,
            1000);

        return new SignedTransaction<NewGroupFeedMessagePayload>(unsignedTx, signature);
    }

    private static SignedTransaction<NewGroupFeedMessagePayload> CreateSignedTransactionWithEmptySender(
        FeedId feedId, int keyGeneration)
    {
        var payload = new NewGroupFeedMessagePayload(
            new FeedMessageId(Guid.NewGuid()),
            feedId,
            "encrypted-content",
            keyGeneration);

        var signature = new SignatureInfo("", "signature"); // Empty sender

        var unsignedTx = new UnsignedTransaction<NewGroupFeedMessagePayload>(
            new TransactionId(Guid.NewGuid()),
            NewGroupFeedMessagePayloadHandler.NewGroupFeedMessagePayloadKind,
            Timestamp.Current,
            payload,
            1000);

        return new SignedTransaction<NewGroupFeedMessagePayload>(unsignedTx, signature);
    }

    private static SignedTransaction<NewGroupFeedMessagePayload> CreateSignedTransactionWithCommitment(
        FeedId feedId, string senderAddress, int keyGeneration, byte[]? authorCommitment)
    {
        var payload = new NewGroupFeedMessagePayload(
            new FeedMessageId(Guid.NewGuid()),
            feedId,
            "encrypted-content",
            keyGeneration,
            ReplyToMessageId: null,
            AuthorCommitment: authorCommitment);

        var signature = new SignatureInfo(senderAddress, "signature");

        var unsignedTx = new UnsignedTransaction<NewGroupFeedMessagePayload>(
            new TransactionId(Guid.NewGuid()),
            NewGroupFeedMessagePayloadHandler.NewGroupFeedMessagePayloadKind,
            Timestamp.Current,
            payload,
            1000);

        return new SignedTransaction<NewGroupFeedMessagePayload>(unsignedTx, signature);
    }

    #endregion
}
