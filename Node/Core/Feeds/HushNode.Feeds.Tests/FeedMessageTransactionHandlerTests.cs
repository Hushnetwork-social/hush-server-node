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
using Microsoft.Extensions.Logging;
using Moq;
using Moq.AutoMock;
using Olimpo;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for FeedMessageTransactionHandler - processes validated personal feed messages.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class FeedMessageTransactionHandlerTests
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
        var messageContent = "test-message-content";

        SetupBlockchainCache(mocker, currentBlockIndex: 100);

        FeedMessage? capturedMessage = null;
        mocker.GetMock<IFeedMessageStorageService>()
            .Setup(x => x.CreateFeedMessageAsync(It.IsAny<FeedMessage>()))
            .Callback<FeedMessage>(msg => capturedMessage = msg)
            .Returns(Task.CompletedTask);

        mocker.GetMock<IEventAggregator>()
            .Setup(x => x.PublishAsync(It.IsAny<NewFeedMessageCreatedEvent>()))
            .Returns(Task.CompletedTask);

        var sut = mocker.CreateInstance<FeedMessageTransactionHandler>();
        var transaction = CreateValidatedTransaction(feedId, messageId, senderAddress, messageContent);

        // Act
        await sut.HandleFeedMessageTransaction(transaction);

        // Assert
        capturedMessage.Should().NotBeNull();
        capturedMessage!.FeedMessageId.Should().Be(messageId);
        capturedMessage.FeedId.Should().Be(feedId);
        capturedMessage.MessageContent.Should().Be(messageContent);
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

        var sut = mocker.CreateInstance<FeedMessageTransactionHandler>();
        var transaction = CreateValidatedTransactionWithReply(
            feedId, messageId, senderAddress, "content", replyToMessageId);

        // Act
        await sut.HandleFeedMessageTransaction(transaction);

        // Assert
        capturedMessage.Should().NotBeNull();
        capturedMessage!.ReplyToMessageId.Should().Be(replyToMessageId);
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

        var sut = mocker.CreateInstance<FeedMessageTransactionHandler>();
        var transaction = CreateValidatedTransaction(feedId, messageId, senderAddress, "content");

        // Act
        await sut.HandleFeedMessageTransaction(transaction);

        // Assert
        mocker.GetMock<IEventAggregator>()
            .Verify(x => x.PublishAsync(It.IsAny<NewFeedMessageCreatedEvent>()), Times.Once);
        capturedEvent.Should().NotBeNull();
        capturedEvent!.FeedMessage.FeedMessageId.Should().Be(messageId);
    }

    #endregion

    #region FEAT-046: Cache Write-Through Tests

    [Fact]
    public async Task HandleFeedMessageTransaction_WritesToBothPostgresAndRedis()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var messageId = new FeedMessageId(Guid.NewGuid());
        var senderAddress = TestDataFactory.CreateAddress();
        var messageContent = "test-message";

        SetupBlockchainCache(mocker, currentBlockIndex: 100);

        FeedMessage? storedMessage = null;
        mocker.GetMock<IFeedMessageStorageService>()
            .Setup(x => x.CreateFeedMessageAsync(It.IsAny<FeedMessage>()))
            .Callback<FeedMessage>(msg => storedMessage = msg)
            .Returns(Task.CompletedTask);

        FeedMessage? cachedMessage = null;
        FeedId? cachedFeedId = null;
        mocker.GetMock<IFeedMessageCacheService>()
            .Setup(x => x.AddMessageAsync(It.IsAny<FeedId>(), It.IsAny<FeedMessage>()))
            .Callback<FeedId, FeedMessage>((fid, msg) =>
            {
                cachedFeedId = fid;
                cachedMessage = msg;
            })
            .Returns(Task.CompletedTask);

        mocker.GetMock<IEventAggregator>()
            .Setup(x => x.PublishAsync(It.IsAny<NewFeedMessageCreatedEvent>()))
            .Returns(Task.CompletedTask);

        var sut = mocker.CreateInstance<FeedMessageTransactionHandler>();
        var transaction = CreateValidatedTransaction(feedId, messageId, senderAddress, messageContent);

        // Act
        await sut.HandleFeedMessageTransaction(transaction);

        // Assert
        storedMessage.Should().NotBeNull("PostgreSQL storage should be called");
        cachedMessage.Should().NotBeNull("Redis cache should be called");
        cachedFeedId.Should().Be(feedId);
        cachedMessage!.FeedMessageId.Should().Be(messageId);
        cachedMessage.MessageContent.Should().Be(messageContent);
    }

    [Fact]
    public async Task HandleFeedMessageTransaction_RedisFailure_StillWritesToPostgres()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var messageId = new FeedMessageId(Guid.NewGuid());
        var senderAddress = TestDataFactory.CreateAddress();

        SetupBlockchainCache(mocker, currentBlockIndex: 100);

        FeedMessage? storedMessage = null;
        mocker.GetMock<IFeedMessageStorageService>()
            .Setup(x => x.CreateFeedMessageAsync(It.IsAny<FeedMessage>()))
            .Callback<FeedMessage>(msg => storedMessage = msg)
            .Returns(Task.CompletedTask);

        // Simulate Redis failure
        mocker.GetMock<IFeedMessageCacheService>()
            .Setup(x => x.AddMessageAsync(It.IsAny<FeedId>(), It.IsAny<FeedMessage>()))
            .ThrowsAsync(new Exception("Redis connection failed"));

        mocker.GetMock<IEventAggregator>()
            .Setup(x => x.PublishAsync(It.IsAny<NewFeedMessageCreatedEvent>()))
            .Returns(Task.CompletedTask);

        var sut = mocker.CreateInstance<FeedMessageTransactionHandler>();
        var transaction = CreateValidatedTransaction(feedId, messageId, senderAddress, "content");

        // Act - should not throw
        await sut.HandleFeedMessageTransaction(transaction);

        // Assert
        storedMessage.Should().NotBeNull("PostgreSQL write should succeed even when Redis fails");
        storedMessage!.FeedMessageId.Should().Be(messageId);
    }

    [Fact]
    public async Task HandleFeedMessageTransaction_RedisFailure_LogsWarning()
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

        // Simulate Redis failure
        var redisException = new Exception("Redis connection failed");
        mocker.GetMock<IFeedMessageCacheService>()
            .Setup(x => x.AddMessageAsync(It.IsAny<FeedId>(), It.IsAny<FeedMessage>()))
            .ThrowsAsync(redisException);

        mocker.GetMock<IEventAggregator>()
            .Setup(x => x.PublishAsync(It.IsAny<NewFeedMessageCreatedEvent>()))
            .Returns(Task.CompletedTask);

        var sut = mocker.CreateInstance<FeedMessageTransactionHandler>();
        var transaction = CreateValidatedTransaction(feedId, messageId, senderAddress, "content");

        // Act
        await sut.HandleFeedMessageTransaction(transaction);

        // Assert - verify logger was called with warning level
        mocker.GetMock<ILogger<FeedMessageTransactionHandler>>()
            .Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.Is<Exception>(ex => ex == redisException),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
    }

    [Fact]
    public async Task HandleFeedMessageTransaction_CacheWriteAfterPostgres()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var messageId = new FeedMessageId(Guid.NewGuid());
        var senderAddress = TestDataFactory.CreateAddress();

        SetupBlockchainCache(mocker, currentBlockIndex: 100);

        var callOrder = new List<string>();

        mocker.GetMock<IFeedMessageStorageService>()
            .Setup(x => x.CreateFeedMessageAsync(It.IsAny<FeedMessage>()))
            .Callback<FeedMessage>(_ => callOrder.Add("PostgreSQL"))
            .Returns(Task.CompletedTask);

        mocker.GetMock<IFeedMessageCacheService>()
            .Setup(x => x.AddMessageAsync(It.IsAny<FeedId>(), It.IsAny<FeedMessage>()))
            .Callback<FeedId, FeedMessage>((_, __) => callOrder.Add("Redis"))
            .Returns(Task.CompletedTask);

        mocker.GetMock<IEventAggregator>()
            .Setup(x => x.PublishAsync(It.IsAny<NewFeedMessageCreatedEvent>()))
            .Returns(Task.CompletedTask);

        var sut = mocker.CreateInstance<FeedMessageTransactionHandler>();
        var transaction = CreateValidatedTransaction(feedId, messageId, senderAddress, "content");

        // Act
        await sut.HandleFeedMessageTransaction(transaction);

        // Assert - PostgreSQL must be called before Redis
        callOrder.Should().ContainInOrder("PostgreSQL", "Redis");
    }

    #endregion

    #region Helper Methods

    private static void SetupBlockchainCache(AutoMocker mocker, long currentBlockIndex)
    {
        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(currentBlockIndex));
    }

    private static ValidatedTransaction<NewFeedMessagePayload> CreateValidatedTransaction(
        FeedId feedId, FeedMessageId messageId, string senderAddress, string messageContent)
    {
        var payload = new NewFeedMessagePayload(
            messageId,
            feedId,
            messageContent);

        var signature = new SignatureInfo(senderAddress, "user-signature");
        var validatorSignature = new SignatureInfo("validator-address", "validator-signature");

        var unsignedTx = new UnsignedTransaction<NewFeedMessagePayload>(
            new TransactionId(Guid.NewGuid()),
            NewFeedMessagePayloadHandler.NewFeedMessagePayloadKind,
            Timestamp.Current,
            payload,
            1000);

        var signedTx = new SignedTransaction<NewFeedMessagePayload>(unsignedTx, signature);
        return new ValidatedTransaction<NewFeedMessagePayload>(signedTx, validatorSignature);
    }

    private static ValidatedTransaction<NewFeedMessagePayload> CreateValidatedTransactionWithReply(
        FeedId feedId, FeedMessageId messageId, string senderAddress, string messageContent,
        FeedMessageId replyToMessageId)
    {
        var payload = new NewFeedMessagePayload(
            messageId,
            feedId,
            messageContent,
            replyToMessageId);

        var signature = new SignatureInfo(senderAddress, "user-signature");
        var validatorSignature = new SignatureInfo("validator-address", "validator-signature");

        var unsignedTx = new UnsignedTransaction<NewFeedMessagePayload>(
            new TransactionId(Guid.NewGuid()),
            NewFeedMessagePayloadHandler.NewFeedMessagePayloadKind,
            Timestamp.Current,
            payload,
            1000);

        var signedTx = new SignedTransaction<NewFeedMessagePayload>(unsignedTx, signature);
        return new ValidatedTransaction<NewFeedMessagePayload>(signedTx, validatorSignature);
    }

    #endregion
}
