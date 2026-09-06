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

public class IndexingDispatcherServiceBlockContextTests
{
    [Fact]
    public async Task BlockContextStrategies_ReceiveTheContainingBlockConsensusTime()
    {
        var eventAggregatorMock = new Mock<IEventAggregator>();
        var blockContextMock = new Mock<IBlockContextIndexStrategy>();
        var tx = CreateTransaction();
        var blockTime = new Timestamp(DateTime.Parse("2026-09-06T10:00:00Z").ToUniversalTime());
        var block = CreateBlockWithTime(blockTime, tx);

        blockContextMock.Setup(x => x.CanHandle(It.IsAny<AbstractTransaction>())).Returns(true);
        DateTime? receivedTime = null;
        blockContextMock
            .Setup(x => x.HandleAsync(It.IsAny<AbstractTransaction>(), It.IsAny<DateTime>()))
            .Returns<AbstractTransaction, DateTime>((_, time) =>
            {
                receivedTime = time;
                return Task.CompletedTask;
            });

        var sut = new IndexingDispatcherService(
            indexStrategies: Array.Empty<IIndexStrategy>(),
            eventAggregator: eventAggregatorMock.Object,
            blockContextStrategies: new[] { blockContextMock.Object });

        await sut.HandleAsync(new BlockCreatedEvent(block));

        receivedTime.Should().Be(blockTime.Value);
        blockContextMock.Verify(x => x.HandleAsync(tx, blockTime.Value), Times.Once);
    }

    [Fact]
    public async Task PlainIndexStrategies_RemainUnchangedWhenBlockContextStrategiesExist()
    {
        var eventAggregatorMock = new Mock<IEventAggregator>();
        var plainMock = new Mock<IIndexStrategy>();
        var blockContextMock = new Mock<IBlockContextIndexStrategy>();
        var tx = CreateTransaction();
        var block = CreateBlock(tx);

        plainMock.Setup(x => x.CanHandle(It.IsAny<AbstractTransaction>())).Returns(true);
        plainMock.Setup(x => x.HandleAsync(It.IsAny<AbstractTransaction>())).Returns(Task.CompletedTask);
        blockContextMock.Setup(x => x.CanHandle(It.IsAny<AbstractTransaction>())).Returns(true);
        blockContextMock.Setup(x => x.HandleAsync(It.IsAny<AbstractTransaction>(), It.IsAny<DateTime>())).Returns(Task.CompletedTask);

        var sut = new IndexingDispatcherService(
            indexStrategies: new[] { plainMock.Object },
            eventAggregator: eventAggregatorMock.Object,
            blockContextStrategies: new[] { blockContextMock.Object });

        await sut.HandleAsync(new BlockCreatedEvent(block));

        plainMock.Verify(x => x.HandleAsync(tx), Times.Once);
        blockContextMock.Verify(x => x.HandleAsync(tx, It.IsAny<DateTime>()), Times.Once);
    }

    private static FinalizedBlock CreateBlockWithTime(Timestamp creationTime, params AbstractTransaction[] transactions)
    {
        var unsignedBlock = new UnsignedBlock(
            new BlockId(Guid.NewGuid()),
            creationTime,
            new BlockIndex(123),
            new BlockId(Guid.NewGuid()),
            new BlockId(Guid.NewGuid()),
            transactions);
        var signed = new SignedBlock(
            unsignedBlock,
            new SignatureInfo("producer", "signature"));
        return new FinalizedBlock(signed, "hash");
    }

    private static FinalizedBlock CreateBlock(params AbstractTransaction[] transactions) =>
        CreateBlockWithTime(Timestamp.Current, transactions);

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
