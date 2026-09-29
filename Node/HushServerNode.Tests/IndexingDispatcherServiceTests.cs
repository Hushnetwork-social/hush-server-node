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
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DurableCompletion_MustFollowHandlers_AndPrecedePublication(bool handlerFails, bool recorderFails)
    {
        var events = new Mock<IEventAggregator>();
        var strategy = new Mock<IIndexStrategy>();
        var recorder = new Mock<IBlockIndexCompletionRecorder>();
        var block = CreateBlock(CreateTransaction());
        var order = new List<string>();
        strategy.Setup(x => x.CanHandle(It.IsAny<AbstractTransaction>())).Returns(true);
        strategy.Setup(x => x.HandleAsync(It.IsAny<AbstractTransaction>())).Returns(() =>
        {
            order.Add("handler");
            return handlerFails ? Task.FromException(new InvalidOperationException("handler fault")) : Task.CompletedTask;
        });
        recorder.Setup(x => x.RecordAsync(block.BlockIndex.Value, block.BlockId.Value, block.Hash, It.IsAny<string>())).Returns(() =>
        {
            order.Add("durable");
            return recorderFails ? Task.FromException(new InvalidOperationException("checkpoint fault")) : Task.CompletedTask;
        });
        events.Setup(x => x.PublishAsync(It.IsAny<BlockIndexCompletedEvent>())).Returns(() =>
        {
            order.Add("published");
            return Task.CompletedTask;
        });
        var dispatcher = new IndexingDispatcherService([strategy.Object], events.Object,
            completionRecorders: [recorder.Object]);
        Func<Task> run = () => dispatcher.HandleAsync(new BlockCreatedEvent(block));
        if (handlerFails || recorderFails) await run.Should().ThrowAsync<InvalidOperationException>();
        else await run();
        order.Should().Equal(handlerFails ? ["handler"] : recorderFails ? ["handler", "durable"] : ["handler", "durable", "published"]);
    }

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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanonicalFrame_PreservesOrderAndRestoresCaller_WithoutCheckpointOnFault(bool failSecond)
    {
        var events = new Mock<IEventAggregator>();
        var strategy = new Mock<IIndexStrategy>();
        strategy.Setup(x => x.CanHandle(It.IsAny<AbstractTransaction>())).Returns(true);
        var first = CreateTransaction();
        var second = CreateTransaction();
        var block = CreateBlock(first, second);
        var observed = new List<BlockTransactionExecution>();
        var outer = new BlockTransactionExecution(Guid.NewGuid(), new(1, DateTime.UtcNow, Guid.NewGuid(), 7));
        using var outerScope = BlockTransactionExecutionScope.Enter(outer);
        strategy.Setup(x => x.HandleAsync(It.IsAny<AbstractTransaction>()))
            .Returns<AbstractTransaction>(async tx =>
            {
                await Task.Yield();
                var frame = BlockTransactionExecutionScope.Current!;
                frame.TransactionId.Should().Be(tx.TransactionId.Value);
                observed.Add(frame);
                if (failSecond && tx.TransactionId == second.TransactionId)
                    throw new InvalidOperationException("controlled indexing fault");
            });
        var checkpoints = 0;
        var dispatcher = new IndexingDispatcherService([strategy.Object], events.Object, () => checkpoints++);
        var run = () => dispatcher.HandleAsync(new BlockCreatedEvent(block));
        if (failSecond) await run.Should().ThrowAsync<InvalidOperationException>();
        else await run();

        observed.Select(x => x.TransactionId).Should().Equal(first.TransactionId.Value, second.TransactionId.Value);
        observed.Select(x => x.Block.TransactionPosition).Should().Equal(0, 1);
        observed.Should().OnlyContain(x => x.Block.BlockId == block.BlockId.Value &&
            x.Block.BlockIndex == block.BlockIndex.Value && x.Block.BlockCreationTimeUtc == block.CreationTimeStamp.Value);
        BlockTransactionExecutionScope.Current.Should().Be(outer);
        checkpoints.Should().Be(failSecond ? 0 : 1);
        events.Verify(x => x.PublishAsync(It.IsAny<BlockIndexCompletedEvent>()), failSecond ? Times.Never() : Times.Once());
    }

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
            .Setup(x => x.HandleAsync(It.IsAny<AbstractTransaction>(), It.IsAny<BlockIndexContext>()))
            .Returns<AbstractTransaction, BlockIndexContext>((_, context) =>
            {
                receivedTime = context.BlockCreationTimeUtc;
                return Task.CompletedTask;
            });

        var sut = new IndexingDispatcherService(
            indexStrategies: Array.Empty<IIndexStrategy>(),
            eventAggregator: eventAggregatorMock.Object,
            blockContextStrategies: new[] { blockContextMock.Object });

        await sut.HandleAsync(new BlockCreatedEvent(block));

        receivedTime.Should().Be(blockTime.Value);
        blockContextMock.Verify(x => x.HandleAsync(tx, It.Is<BlockIndexContext>(c => c.BlockCreationTimeUtc == blockTime.Value)), Times.Once);
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
        blockContextMock.Setup(x => x.HandleAsync(It.IsAny<AbstractTransaction>(), It.IsAny<BlockIndexContext>())).Returns(Task.CompletedTask);

        var sut = new IndexingDispatcherService(
            indexStrategies: new[] { plainMock.Object },
            eventAggregator: eventAggregatorMock.Object,
            blockContextStrategies: new[] { blockContextMock.Object });

        await sut.HandleAsync(new BlockCreatedEvent(block));

        plainMock.Verify(x => x.HandleAsync(tx), Times.Once);
        blockContextMock.Verify(x => x.HandleAsync(tx, It.IsAny<BlockIndexContext>()), Times.Once);
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
