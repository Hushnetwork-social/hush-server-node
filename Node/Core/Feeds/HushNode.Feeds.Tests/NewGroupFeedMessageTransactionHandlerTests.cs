using FluentAssertions;
using HushNode.Caching;
using HushNode.Events;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using Moq;
using Moq.AutoMock;
using Olimpo;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for NewGroupFeedMessageTransactionHandler - processes validated group messages.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class NewGroupFeedMessageTransactionHandlerTests
{
    #region Message Storage Tests

    [Fact]
    public async Task HandleAsync_ValidMessage_StoresMessage()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var messageId = new FeedMessageId(Guid.NewGuid());
        var senderAddress = TestDataFactory.CreateAddress();
        var encryptedContent = "encrypted-message-content";

        SetupBlockchainCache(mocker, currentBlockIndex: 100);

        FeedMessage? capturedMessage = null;
        mocker.GetMock<IFeedMessageStorageService>()
            .Setup(x => x.CreateFeedMessageAsync(It.IsAny<FeedMessage>()))
            .Callback<FeedMessage>(msg => capturedMessage = msg)
            .Returns(Task.CompletedTask);

        mocker.GetMock<IEventAggregator>()
            .Setup(x => x.PublishAsync(It.IsAny<NewFeedMessageCreatedEvent>()))
            .Returns(Task.CompletedTask);

        var sut = mocker.CreateInstance<NewGroupFeedMessageTransactionHandler>();
        var transaction = CreateValidatedTransaction(feedId, messageId, senderAddress, encryptedContent);

        // Act
        await sut.HandleGroupFeedMessageTransaction(transaction);

        // Assert
        capturedMessage.Should().NotBeNull();
        capturedMessage!.FeedMessageId.Should().Be(messageId);
        capturedMessage.FeedId.Should().Be(feedId);
        capturedMessage.MessageContent.Should().Be(encryptedContent);
        capturedMessage.IssuerPublicAddress.Should().Be(senderAddress);
        capturedMessage.BlockIndex.Should().Be(new BlockIndex(100));
    }

    [Fact]
    public async Task HandleAsync_WithReplyToMessageId_StoresCorrectly()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var messageId = new FeedMessageId(Guid.NewGuid());
        var replyToMessageId = new FeedMessageId(Guid.NewGuid());
        var senderAddress = TestDataFactory.CreateAddress();

        SetupBlockchainCache(mocker, currentBlockIndex: 100);

        FeedMessage? capturedMessage = null;
        mocker.GetMock<IFeedMessageStorageService>()
            .Setup(x => x.CreateFeedMessageAsync(It.IsAny<FeedMessage>()))
            .Callback<FeedMessage>(msg => capturedMessage = msg)
            .Returns(Task.CompletedTask);

        mocker.GetMock<IEventAggregator>()
            .Setup(x => x.PublishAsync(It.IsAny<NewFeedMessageCreatedEvent>()))
            .Returns(Task.CompletedTask);

        var sut = mocker.CreateInstance<NewGroupFeedMessageTransactionHandler>();
        var transaction = CreateValidatedTransactionWithReply(
            feedId, messageId, senderAddress, "content", replyToMessageId);

        // Act
        await sut.HandleGroupFeedMessageTransaction(transaction);

        // Assert
        capturedMessage.Should().NotBeNull();
        capturedMessage!.ReplyToMessageId.Should().Be(replyToMessageId);
    }

    [Fact]
    public async Task HandleAsync_WithAuthorCommitment_StoresCorrectly()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var messageId = new FeedMessageId(Guid.NewGuid());
        var senderAddress = TestDataFactory.CreateAddress();
        var authorCommitment = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        SetupBlockchainCache(mocker, currentBlockIndex: 100);

        FeedMessage? capturedMessage = null;
        mocker.GetMock<IFeedMessageStorageService>()
            .Setup(x => x.CreateFeedMessageAsync(It.IsAny<FeedMessage>()))
            .Callback<FeedMessage>(msg => capturedMessage = msg)
            .Returns(Task.CompletedTask);

        mocker.GetMock<IEventAggregator>()
            .Setup(x => x.PublishAsync(It.IsAny<NewFeedMessageCreatedEvent>()))
            .Returns(Task.CompletedTask);

        var sut = mocker.CreateInstance<NewGroupFeedMessageTransactionHandler>();
        var transaction = CreateValidatedTransactionWithAuthorCommitment(
            feedId, messageId, senderAddress, "content", authorCommitment);

        // Act
        await sut.HandleGroupFeedMessageTransaction(transaction);

        // Assert
        capturedMessage.Should().NotBeNull();
        capturedMessage!.AuthorCommitment.Should().BeEquivalentTo(authorCommitment);
    }

    #endregion

    #region Event Publishing Tests

    [Fact]
    public async Task HandleAsync_PublishesNewFeedMessageCreatedEvent()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var messageId = new FeedMessageId(Guid.NewGuid());
        var senderAddress = TestDataFactory.CreateAddress();

        SetupBlockchainCache(mocker, currentBlockIndex: 100);

        mocker.GetMock<IFeedMessageStorageService>()
            .Setup(x => x.CreateFeedMessageAsync(It.IsAny<FeedMessage>()))
            .Returns(Task.CompletedTask);

        NewFeedMessageCreatedEvent? capturedEvent = null;
        mocker.GetMock<IEventAggregator>()
            .Setup(x => x.PublishAsync(It.IsAny<NewFeedMessageCreatedEvent>()))
            .Callback<NewFeedMessageCreatedEvent>(evt => capturedEvent = evt)
            .Returns(Task.CompletedTask);

        var sut = mocker.CreateInstance<NewGroupFeedMessageTransactionHandler>();
        var transaction = CreateValidatedTransaction(feedId, messageId, senderAddress, "content");

        // Act
        await sut.HandleGroupFeedMessageTransaction(transaction);

        // Assert
        mocker.GetMock<IEventAggregator>()
            .Verify(x => x.PublishAsync(It.IsAny<NewFeedMessageCreatedEvent>()), Times.Once);
        capturedEvent.Should().NotBeNull();
        capturedEvent!.FeedMessage.FeedMessageId.Should().Be(messageId);
    }

    [Fact]
    public async Task HandleAsync_ExtractsSenderFromSignature()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var messageId = new FeedMessageId(Guid.NewGuid());
        var expectedSenderAddress = TestDataFactory.CreateAddress();

        SetupBlockchainCache(mocker, currentBlockIndex: 100);

        FeedMessage? capturedMessage = null;
        mocker.GetMock<IFeedMessageStorageService>()
            .Setup(x => x.CreateFeedMessageAsync(It.IsAny<FeedMessage>()))
            .Callback<FeedMessage>(msg => capturedMessage = msg)
            .Returns(Task.CompletedTask);

        mocker.GetMock<IEventAggregator>()
            .Setup(x => x.PublishAsync(It.IsAny<NewFeedMessageCreatedEvent>()))
            .Returns(Task.CompletedTask);

        var sut = mocker.CreateInstance<NewGroupFeedMessageTransactionHandler>();
        var transaction = CreateValidatedTransaction(feedId, messageId, expectedSenderAddress, "content");

        // Act
        await sut.HandleGroupFeedMessageTransaction(transaction);

        // Assert
        capturedMessage.Should().NotBeNull();
        capturedMessage!.IssuerPublicAddress.Should().Be(expectedSenderAddress);
    }

    #endregion

    #region Helper Methods

    private static void SetupBlockchainCache(AutoMocker mocker, long currentBlockIndex)
    {
        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(currentBlockIndex));
    }

    private static ValidatedTransaction<NewGroupFeedMessagePayload> CreateValidatedTransaction(
        FeedId feedId, FeedMessageId messageId, string senderAddress, string encryptedContent)
    {
        var payload = new NewGroupFeedMessagePayload(
            messageId,
            feedId,
            encryptedContent,
            KeyGeneration: 1);

        var signature = new SignatureInfo(senderAddress, "user-signature");
        var validatorSignature = new SignatureInfo("validator-address", "validator-signature");

        var unsignedTx = new UnsignedTransaction<NewGroupFeedMessagePayload>(
            new TransactionId(Guid.NewGuid()),
            NewGroupFeedMessagePayloadHandler.NewGroupFeedMessagePayloadKind,
            Timestamp.Current,
            payload,
            1000);

        var signedTx = new SignedTransaction<NewGroupFeedMessagePayload>(unsignedTx, signature);
        return new ValidatedTransaction<NewGroupFeedMessagePayload>(signedTx, validatorSignature);
    }

    private static ValidatedTransaction<NewGroupFeedMessagePayload> CreateValidatedTransactionWithReply(
        FeedId feedId, FeedMessageId messageId, string senderAddress, string encryptedContent,
        FeedMessageId replyToMessageId)
    {
        var payload = new NewGroupFeedMessagePayload(
            messageId,
            feedId,
            encryptedContent,
            KeyGeneration: 1,
            ReplyToMessageId: replyToMessageId);

        var signature = new SignatureInfo(senderAddress, "user-signature");
        var validatorSignature = new SignatureInfo("validator-address", "validator-signature");

        var unsignedTx = new UnsignedTransaction<NewGroupFeedMessagePayload>(
            new TransactionId(Guid.NewGuid()),
            NewGroupFeedMessagePayloadHandler.NewGroupFeedMessagePayloadKind,
            Timestamp.Current,
            payload,
            1000);

        var signedTx = new SignedTransaction<NewGroupFeedMessagePayload>(unsignedTx, signature);
        return new ValidatedTransaction<NewGroupFeedMessagePayload>(signedTx, validatorSignature);
    }

    private static ValidatedTransaction<NewGroupFeedMessagePayload> CreateValidatedTransactionWithAuthorCommitment(
        FeedId feedId, FeedMessageId messageId, string senderAddress, string encryptedContent,
        byte[] authorCommitment)
    {
        var payload = new NewGroupFeedMessagePayload(
            messageId,
            feedId,
            encryptedContent,
            KeyGeneration: 1,
            AuthorCommitment: authorCommitment);

        var signature = new SignatureInfo(senderAddress, "user-signature");
        var validatorSignature = new SignatureInfo("validator-address", "validator-signature");

        var unsignedTx = new UnsignedTransaction<NewGroupFeedMessagePayload>(
            new TransactionId(Guid.NewGuid()),
            NewGroupFeedMessagePayloadHandler.NewGroupFeedMessagePayloadKind,
            Timestamp.Current,
            payload,
            1000);

        var signedTx = new SignedTransaction<NewGroupFeedMessagePayload>(unsignedTx, signature);
        return new ValidatedTransaction<NewGroupFeedMessagePayload>(signedTx, validatorSignature);
    }

    #endregion
}
