using FluentAssertions;
using HushNode.Blockchain.BlockModel.States;
using HushNode.Events;
using HushNode.Indexing;
using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using Moq;
using Olimpo;
using Xunit;

namespace HushServerNode.Tests;

public class IndexingDispatcherServiceTests
{
    [Fact]
    public async Task HandleAsync_ShouldProcessTransactionsSequentially_AndPublishCompletionAfterAllHandlers()
    {
        // Arrange
        var eventAggregatorMock = new Mock<IEventAggregator>();
        var strategyMock = new Mock<IIndexStrategy>();

        var tx1 = CreateTransaction();
        var tx2 = CreateTransaction();
        var block = CreateBlock(tx1, tx2);

        strategyMock
            .Setup(x => x.CanHandle(It.IsAny<AbstractTransaction>()))
            .Returns(true);

        var firstTransactionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstTransactionToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondTransactionStarted = false;

        strategyMock
            .Setup(x => x.HandleAsync(It.IsAny<AbstractTransaction>()))
            .Returns<AbstractTransaction>(async tx =>
            {
                if (tx.TransactionId == tx1.TransactionId)
                {
                    firstTransactionStarted.TrySetResult();
                    await allowFirstTransactionToFinish.Task;
                }
                else if (tx.TransactionId == tx2.TransactionId)
                {
                    secondTransactionStarted = true;
                }
            });

        var sut = new IndexingDispatcherService(
            [strategyMock.Object],
            eventAggregatorMock.Object);

        // Act
        var handleTask = sut.HandleAsync(new BlockCreatedEvent(block));
        await firstTransactionStarted.Task;

        // Assert - second transaction must not start before first finishes
        secondTransactionStarted.Should().BeFalse();

        allowFirstTransactionToFinish.TrySetResult();
        await handleTask;

        secondTransactionStarted.Should().BeTrue();
        eventAggregatorMock.Verify(
            x => x.PublishAsync(It.Is<BlockIndexCompletedEvent>(evt => evt.BlockIndex == block.BlockIndex)),
            Times.Once);
    }

    private static FinalizedBlock CreateBlock(params AbstractTransaction[] transactions)
    {
        var unsignedBlock = new UnsignedBlock(
            new BlockId(Guid.NewGuid()),
            Timestamp.Current,
            new BlockIndex(123),
            new BlockId(Guid.NewGuid()),
            BlockId.Empty,
            transactions);

        var signedBlock = new SignedBlock(
            unsignedBlock,
            new SignatureInfo("validator", "signature"));

        return new FinalizedBlock(signedBlock, "block-hash");
    }

    private static AbstractTransaction CreateTransaction()
    {
        return new UnsignedTransaction<DummyPayload>(
            new TransactionId(Guid.NewGuid()),
            Guid.NewGuid(),
            Timestamp.Current,
            new DummyPayload(),
            payloadSize: 0);
    }

    private sealed record DummyPayload : ITransactionPayloadKind;
}
