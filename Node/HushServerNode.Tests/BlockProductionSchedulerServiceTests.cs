using System.Reactive.Linq;
using System.Reactive.Subjects;
using FluentAssertions;
using HushNode.Blockchain.BlockModel.States;
using HushNode.Blockchain.Configuration;
using HushNode.Blockchain.Services;
using HushNode.Blockchain.Storage;
using HushNode.Blockchain.Workflows;
using HushNode.Caching;
using HushNode.Events;
using HushNode.MemPool;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Olimpo;
using Xunit;

namespace HushServerNode.Tests;

/// <summary>
/// Unit tests for BlockProductionSchedulerService - verifies injectable observable factory,
/// test mode behavior, and finalization callback integration.
/// </summary>
public class BlockProductionSchedulerServiceTests
{
    #region Factory Injection Tests

    [Fact]
    public void Constructor_WithoutFactory_ShouldUseDefaultInterval()
    {
        // Arrange
        var mocks = CreateMocks();

        // Act
        var service = new BlockProductionSchedulerService(
            mocks.BlockAssembler,
            mocks.MemPool,
            mocks.BlockchainStorage,
            mocks.BlockchainCache,
            mocks.EventAggregator,
            mocks.BlockchainSettings,
            mocks.Logger);

        // Assert
        // Service should be created successfully - no exception
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithFactory_ShouldUseProvidedObservable()
    {
        // Arrange
        var mocks = CreateMocks();
        var testSubject = new Subject<long>();
        var observableFactoryCalled = false;
        Func<IObservable<long>> factory = () =>
        {
            observableFactoryCalled = true;
            return testSubject.AsObservable();
        };

        // Act
        var service = new BlockProductionSchedulerService(
            mocks.BlockAssembler,
            mocks.MemPool,
            mocks.BlockchainStorage,
            mocks.BlockchainCache,
            mocks.EventAggregator,
            mocks.BlockchainSettings,
            mocks.Logger,
            factory);

        // Assert
        observableFactoryCalled.Should().BeTrue();
        service.Should().NotBeNull();
    }

    #endregion

    #region Finalization Callback Tests

    [Fact]
    public async Task HandleAsync_BlockIndexCompletedEvent_ShouldInvokeCallback()
    {
        // Arrange
        var mocks = CreateMocks();
        var testSubject = new Subject<long>();
        var callbackInvoked = false;
        Action callback = () => callbackInvoked = true;

        var service = new BlockProductionSchedulerService(
            mocks.BlockAssembler,
            mocks.MemPool,
            mocks.BlockchainStorage,
            mocks.BlockchainCache,
            mocks.EventAggregator,
            mocks.BlockchainSettings,
            mocks.Logger,
            () => testSubject.AsObservable(),
            callback);

        // Act
        await service.HandleAsync(new BlockIndexCompletedEvent(new BlockIndex(1)));

        // Assert
        callbackInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_BlockCreatedEvent_WithoutCallback_ShouldNotThrow()
    {
        // Arrange
        var mocks = CreateMocks();
        var service = new BlockProductionSchedulerService(
            mocks.BlockAssembler,
            mocks.MemPool,
            mocks.BlockchainStorage,
            mocks.BlockchainCache,
            mocks.EventAggregator,
            mocks.BlockchainSettings,
            mocks.Logger);

        // Act
        var act = async () => await service.HandleAsync(new BlockCreatedEvent(CreateMockFinalizedBlock()));

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task HandleAsync_BlockCreatedEvent_WithNullCallback_ShouldNotThrow()
    {
        // Arrange
        var mocks = CreateMocks();
        var testSubject = new Subject<long>();

        var service = new BlockProductionSchedulerService(
            mocks.BlockAssembler,
            mocks.MemPool,
            mocks.BlockchainStorage,
            mocks.BlockchainCache,
            mocks.EventAggregator,
            mocks.BlockchainSettings,
            mocks.Logger,
            () => testSubject.AsObservable(),
            onBlockFinalized: null);

        // Act
        var act = async () => await service.HandleAsync(new BlockCreatedEvent(CreateMockFinalizedBlock()));

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task HandleAsync_BlockCreatedEvent_ShouldNotInvokeCallback()
    {
        // Arrange - BlockCreatedEvent should NOT invoke callback (that's BlockIndexCompletedEvent's job)
        var mocks = CreateMocks();
        var testSubject = new Subject<long>();
        var callbackInvoked = false;
        Action callback = () => callbackInvoked = true;

        var service = new BlockProductionSchedulerService(
            mocks.BlockAssembler,
            mocks.MemPool,
            mocks.BlockchainStorage,
            mocks.BlockchainCache,
            mocks.EventAggregator,
            mocks.BlockchainSettings,
            mocks.Logger,
            () => testSubject.AsObservable(),
            callback);

        // Act
        await service.HandleAsync(new BlockCreatedEvent(CreateMockFinalizedBlock()));

        // Assert
        callbackInvoked.Should().BeFalse("BlockCreatedEvent should not invoke callback - only BlockIndexCompletedEvent should");
    }

    [Fact]
    public async Task HandleAsync_BlockIndexCompletedEvent_WithNullCallback_ShouldNotThrow()
    {
        // Arrange
        var mocks = CreateMocks();
        var testSubject = new Subject<long>();

        var service = new BlockProductionSchedulerService(
            mocks.BlockAssembler,
            mocks.MemPool,
            mocks.BlockchainStorage,
            mocks.BlockchainCache,
            mocks.EventAggregator,
            mocks.BlockchainSettings,
            mocks.Logger,
            () => testSubject.AsObservable(),
            onBlockFinalized: null);

        // Act
        var act = async () => await service.HandleAsync(new BlockIndexCompletedEvent(new BlockIndex(1)));

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Integration with BlockProductionControl Tests

    [Fact]
    public async Task Integration_WithBlockProductionControl_ShouldCompleteWhenIndexingFinalized()
    {
        // Arrange
        var mocks = CreateMocks();
        using var control = new Testing.BlockProductionControl();
        var (observableFactory, onBlockFinalized) = control.GetSchedulerConfiguration();

        var service = new BlockProductionSchedulerService(
            mocks.BlockAssembler,
            mocks.MemPool,
            mocks.BlockchainStorage,
            mocks.BlockchainCache,
            mocks.EventAggregator,
            mocks.BlockchainSettings,
            mocks.Logger,
            observableFactory,
            onBlockFinalized);

        // Subscribe to the observable to capture when block production is triggered
        bool triggerReceived = false;
        using var subscription = control.Observable.Subscribe(_ =>
        {
            triggerReceived = true;
        });

        // Act
        var produceTask = control.ProduceBlockAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50); // Let it start

        // Simulate indexing completion by calling HandleAsync with BlockIndexCompletedEvent
        await service.HandleAsync(new BlockIndexCompletedEvent(new BlockIndex(1)));

        // Assert
        await produceTask;
        triggerReceived.Should().BeTrue();
        produceTask.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void GetSchedulerConfiguration_ShouldReturnValidFactoryAndCallback()
    {
        // Arrange
        using var control = new Testing.BlockProductionControl();

        // Act
        var (observableFactory, onBlockFinalized) = control.GetSchedulerConfiguration();

        // Assert
        observableFactory.Should().NotBeNull();
        onBlockFinalized.Should().NotBeNull();

        // Verify they work
        var receivedValues = new List<long>();
        using var subscription = observableFactory().Subscribe(v => receivedValues.Add(v));
        control.TriggerBlockProduction();
        receivedValues.Should().HaveCount(1);
    }

    #endregion

    #region Helper Methods

    private static ServiceMocks CreateMocks()
    {
        var blockAssembler = new Mock<IBlockAssemblerWorkflow>();
        var memPool = new Mock<IMemPoolService>();
        var blockchainStorage = new Mock<IBlockchainStorageService>();
        var blockchainCache = new Mock<IBlockchainCache>();
        var eventAggregator = new Mock<IEventAggregator>();
        var logger = new Mock<ILogger<BlockProductionSchedulerService>>();

        var settings = new BlockchainSettings
        {
            MaxEmptyBlocksBeforePause = 10
        };
        var blockchainSettings = Options.Create(settings);

        memPool.Setup(m => m.GetPendingValidatedTransactionsAsync())
            .Returns(Enumerable.Empty<AbstractTransaction>());

        return new ServiceMocks(
            blockAssembler.Object,
            memPool.Object,
            blockchainStorage.Object,
            blockchainCache.Object,
            eventAggregator.Object,
            blockchainSettings,
            logger.Object);
    }

    private static FinalizedBlock CreateMockFinalizedBlock()
    {
        var blockId = new BlockId(Guid.NewGuid());
        var timestamp = new Timestamp(DateTime.UtcNow);
        var blockIndex = new BlockIndex(1);
        var previousBlockId = new BlockId(Guid.NewGuid());
        var nextBlockId = new BlockId(Guid.NewGuid());
        var signatureInfo = new SignatureInfo("test-public-key", "test-signature");
        var transactions = Array.Empty<AbstractTransaction>();

        var unsignedBlock = new UnsignedBlock(
            blockId,
            timestamp,
            blockIndex,
            previousBlockId,
            nextBlockId,
            transactions);

        var signedBlock = new SignedBlock(unsignedBlock, signatureInfo);

        return signedBlock.FinalizeIt();
    }

    private sealed record ServiceMocks(
        IBlockAssemblerWorkflow BlockAssembler,
        IMemPoolService MemPool,
        IBlockchainStorageService BlockchainStorage,
        IBlockchainCache BlockchainCache,
        IEventAggregator EventAggregator,
        IOptions<BlockchainSettings> BlockchainSettings,
        ILogger<BlockProductionSchedulerService> Logger);

    #endregion
}
