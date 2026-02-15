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
/// Tests for FeedMessageTransactionHandler attachment persistence (FEAT-066).
/// </summary>
public class FeedMessageTransactionHandlerAttachmentTests
{
    [Fact]
    public async Task HandleAsync_MessageWithAttachments_PersistsAllAndDeletesTemp()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var messageId = new FeedMessageId(Guid.NewGuid());
        var senderAddress = TestDataFactory.CreateAddress();
        var att1Id = Guid.NewGuid().ToString();
        var att2Id = Guid.NewGuid().ToString();

        var attachments = new List<AttachmentReference>
        {
            new(att1Id, new string('a', 64), "image/jpeg", 1024, "photo.jpg"),
            new(att2Id, new string('b', 64), "image/png", 2048, "image.png"),
        };

        SetupBlockchainCache(mocker, 100);

        mocker.GetMock<IAttachmentTempStorageService>()
            .Setup(x => x.RetrieveAsync(att1Id))
            .ReturnsAsync((new byte[] { 1, 2, 3 }, new byte[] { 10, 20 }));
        mocker.GetMock<IAttachmentTempStorageService>()
            .Setup(x => x.RetrieveAsync(att2Id))
            .ReturnsAsync((new byte[] { 4, 5, 6 }, (byte[]?)null));

        mocker.GetMock<IEventAggregator>()
            .Setup(x => x.PublishAsync(It.IsAny<NewFeedMessageCreatedEvent>()))
            .Returns(Task.CompletedTask);

        var sut = mocker.CreateInstance<FeedMessageTransactionHandler>();
        var transaction = CreateValidatedTransaction(feedId, messageId, senderAddress, "test", attachments);

        // Act
        await sut.HandleFeedMessageTransaction(transaction);

        // Assert - both attachments persisted to storage
        mocker.GetMock<IAttachmentStorageService>()
            .Verify(x => x.CreateAttachmentAsync(It.Is<AttachmentEntity>(e => e.Id == att1Id)), Times.Once);
        mocker.GetMock<IAttachmentStorageService>()
            .Verify(x => x.CreateAttachmentAsync(It.Is<AttachmentEntity>(e => e.Id == att2Id)), Times.Once);

        // Assert - both temp files deleted
        mocker.GetMock<IAttachmentTempStorageService>()
            .Verify(x => x.DeleteAsync(att1Id), Times.Once);
        mocker.GetMock<IAttachmentTempStorageService>()
            .Verify(x => x.DeleteAsync(att2Id), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_MessageWithoutAttachments_NoAttachmentOperations()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var messageId = new FeedMessageId(Guid.NewGuid());
        var senderAddress = TestDataFactory.CreateAddress();

        SetupBlockchainCache(mocker, 100);

        mocker.GetMock<IEventAggregator>()
            .Setup(x => x.PublishAsync(It.IsAny<NewFeedMessageCreatedEvent>()))
            .Returns(Task.CompletedTask);

        var sut = mocker.CreateInstance<FeedMessageTransactionHandler>();
        var transaction = CreateValidatedTransaction(feedId, messageId, senderAddress, "plain text");

        // Act
        await sut.HandleFeedMessageTransaction(transaction);

        // Assert - message still persisted normally
        mocker.GetMock<IFeedMessageStorageService>()
            .Verify(x => x.CreateFeedMessageAsync(It.IsAny<FeedMessage>()), Times.Once);

        // Assert - no attachment operations
        mocker.GetMock<IAttachmentTempStorageService>()
            .Verify(x => x.RetrieveAsync(It.IsAny<string>()), Times.Never);
        mocker.GetMock<IAttachmentStorageService>()
            .Verify(x => x.CreateAttachmentAsync(It.IsAny<AttachmentEntity>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MissingTempFile_LogsWarningAndContinues()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = TestDataFactory.CreateFeedId();
        var messageId = new FeedMessageId(Guid.NewGuid());
        var senderAddress = TestDataFactory.CreateAddress();
        var missingAttId = Guid.NewGuid().ToString();

        var attachments = new List<AttachmentReference>
        {
            new(missingAttId, new string('c', 64), "application/pdf", 5000, "doc.pdf"),
        };

        SetupBlockchainCache(mocker, 100);

        mocker.GetMock<IAttachmentTempStorageService>()
            .Setup(x => x.RetrieveAsync(missingAttId))
            .ReturnsAsync(((byte[]?, byte[]?)?)null);

        mocker.GetMock<IEventAggregator>()
            .Setup(x => x.PublishAsync(It.IsAny<NewFeedMessageCreatedEvent>()))
            .Returns(Task.CompletedTask);

        var sut = mocker.CreateInstance<FeedMessageTransactionHandler>();
        var transaction = CreateValidatedTransaction(feedId, messageId, senderAddress, "with attachment", attachments);

        // Act
        await sut.HandleFeedMessageTransaction(transaction);

        // Assert - message still persisted
        mocker.GetMock<IFeedMessageStorageService>()
            .Verify(x => x.CreateFeedMessageAsync(It.IsAny<FeedMessage>()), Times.Once);

        // Assert - no attachment created (temp was missing)
        mocker.GetMock<IAttachmentStorageService>()
            .Verify(x => x.CreateAttachmentAsync(It.IsAny<AttachmentEntity>()), Times.Never);
    }

    #region Helper Methods

    private static void SetupBlockchainCache(AutoMocker mocker, long blockIndex)
    {
        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(blockIndex));
    }

    private static ValidatedTransaction<NewFeedMessagePayload> CreateValidatedTransaction(
        FeedId feedId,
        FeedMessageId messageId,
        string senderAddress,
        string content,
        List<AttachmentReference>? attachments = null)
    {
        var payload = new NewFeedMessagePayload(messageId, feedId, content, Attachments: attachments);

        var signature = new SignatureInfo(senderAddress, "fake-signature");
        var validatorSignature = new SignatureInfo("validator-address", "validator-signature");

        var unsignedTx = new UnsignedTransaction<NewFeedMessagePayload>(
            new TransactionId(Guid.NewGuid()),
            NewFeedMessagePayloadHandler.NewFeedMessagePayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            1000);

        var signedTx = new SignedTransaction<NewFeedMessagePayload>(unsignedTx, signature);
        return new ValidatedTransaction<NewFeedMessagePayload>(signedTx, validatorSignature);
    }

    #endregion
}
