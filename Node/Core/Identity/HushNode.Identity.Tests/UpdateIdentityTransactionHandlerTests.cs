using FluentAssertions;
using HushNode.Caching;
using HushNode.Events;
using HushNode.Feeds.Storage;
using HushNode.Identity.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;
using Microsoft.Extensions.Logging;
using Moq;
using Olimpo;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushNode.Identity.Tests;

/// <summary>
/// Tests for UpdateIdentityTransactionHandler - verifies that identity updates
/// correctly publish IdentityUpdatedEvent for cache invalidation.
/// </summary>
public class UpdateIdentityTransactionHandlerTests
{
    private const string TestAddress = "04abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";

    #region Event Publication Tests

    [Fact]
    public async Task HandleUpdateIdentityTransaction_SuccessfulUpdate_PublishesIdentityUpdatedEvent()
    {
        // Arrange
        var (sut, eventAggregatorMock, identityRepoMock, _, _) = CreateHandler();
        var transaction = CreateValidTransaction("NewAlias");

        identityRepoMock
            .Setup(x => x.AnyAsync(TestAddress))
            .ReturnsAsync(true);

        // Act
        await sut.HandleUpdateIdentityTransaction(transaction);

        // Assert
        eventAggregatorMock.Verify(
            x => x.PublishAsync(It.Is<IdentityUpdatedEvent>(e => e.PublicSigningAddress == TestAddress)),
            Times.Once);
    }

    [Fact]
    public async Task HandleUpdateIdentityTransaction_SuccessfulUpdate_EventContainsCorrectAddress()
    {
        // Arrange
        var (sut, eventAggregatorMock, identityRepoMock, _, _) = CreateHandler();
        var transaction = CreateValidTransaction("NewAlias");
        IdentityUpdatedEvent? capturedEvent = null;

        identityRepoMock
            .Setup(x => x.AnyAsync(TestAddress))
            .ReturnsAsync(true);

        eventAggregatorMock
            .Setup(x => x.PublishAsync(It.IsAny<IdentityUpdatedEvent>()))
            .Callback<IdentityUpdatedEvent>(e => capturedEvent = e)
            .Returns(Task.CompletedTask);

        // Act
        await sut.HandleUpdateIdentityTransaction(transaction);

        // Assert
        capturedEvent.Should().NotBeNull();
        capturedEvent!.PublicSigningAddress.Should().Be(TestAddress);
    }

    [Fact]
    public async Task HandleUpdateIdentityTransaction_NonExistentIdentity_DoesNotPublishEvent()
    {
        // Arrange
        var (sut, eventAggregatorMock, identityRepoMock, _, _) = CreateHandler();
        var transaction = CreateValidTransaction("NewAlias");

        identityRepoMock
            .Setup(x => x.AnyAsync(TestAddress))
            .ReturnsAsync(false);

        // Act
        await sut.HandleUpdateIdentityTransaction(transaction);

        // Assert
        eventAggregatorMock.Verify(
            x => x.PublishAsync(It.IsAny<IdentityUpdatedEvent>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleUpdateIdentityTransaction_EmptyAlias_DoesNotPublishEvent()
    {
        // Arrange
        var (sut, eventAggregatorMock, _, _, _) = CreateHandler();
        var transaction = CreateValidTransaction("");

        // Act
        await sut.HandleUpdateIdentityTransaction(transaction);

        // Assert
        eventAggregatorMock.Verify(
            x => x.PublishAsync(It.IsAny<IdentityUpdatedEvent>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleUpdateIdentityTransaction_WhitespaceAlias_DoesNotPublishEvent()
    {
        // Arrange
        var (sut, eventAggregatorMock, _, _, _) = CreateHandler();
        var transaction = CreateValidTransaction("   ");

        // Act
        await sut.HandleUpdateIdentityTransaction(transaction);

        // Assert
        eventAggregatorMock.Verify(
            x => x.PublishAsync(It.IsAny<IdentityUpdatedEvent>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleUpdateIdentityTransaction_NullAlias_DoesNotPublishEvent()
    {
        // Arrange
        var (sut, eventAggregatorMock, _, _, _) = CreateHandler();
        var transaction = CreateValidTransaction(null!);

        // Act
        await sut.HandleUpdateIdentityTransaction(transaction);

        // Assert
        eventAggregatorMock.Verify(
            x => x.PublishAsync(It.IsAny<IdentityUpdatedEvent>()),
            Times.Never);
    }

    #endregion

    #region Helper Methods

    private static (
        UpdateIdentityTransactionHandler sut,
        Mock<IEventAggregator> eventAggregatorMock,
        Mock<IIdentityRepository> identityRepoMock,
        Mock<IFeedsStorageService> feedsServiceMock,
        Mock<IBlockchainCache> blockchainCacheMock)
        CreateHandler()
    {
        var unitOfWorkProviderMock = new Mock<IUnitOfWorkProvider<IdentityDbContext>>();
        var readonlyUnitOfWorkMock = new Mock<IReadOnlyUnitOfWork<IdentityDbContext>>();
        var writableUnitOfWorkMock = new Mock<IWritableUnitOfWork<IdentityDbContext>>();
        var identityRepoMock = new Mock<IIdentityRepository>();
        var feedsServiceMock = new Mock<IFeedsStorageService>();
        var blockchainCacheMock = new Mock<IBlockchainCache>();
        var eventAggregatorMock = new Mock<IEventAggregator>();
        var loggerMock = new Mock<ILogger<UpdateIdentityTransactionHandler>>();

        blockchainCacheMock
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(100));

        unitOfWorkProviderMock
            .Setup(x => x.CreateReadOnly())
            .Returns(readonlyUnitOfWorkMock.Object);

        unitOfWorkProviderMock
            .Setup(x => x.CreateWritable())
            .Returns(writableUnitOfWorkMock.Object);

        readonlyUnitOfWorkMock
            .Setup(x => x.GetRepository<IIdentityRepository>())
            .Returns(identityRepoMock.Object);

        writableUnitOfWorkMock
            .Setup(x => x.GetRepository<IIdentityRepository>())
            .Returns(identityRepoMock.Object);

        writableUnitOfWorkMock
            .Setup(x => x.CommitAsync())
            .Returns(Task.CompletedTask);

        var sut = new UpdateIdentityTransactionHandler(
            unitOfWorkProviderMock.Object,
            feedsServiceMock.Object,
            blockchainCacheMock.Object,
            eventAggregatorMock.Object,
            loggerMock.Object);

        return (sut, eventAggregatorMock, identityRepoMock, feedsServiceMock, blockchainCacheMock);
    }

    private static ValidatedTransaction<UpdateIdentityPayload> CreateValidTransaction(string newAlias)
    {
        var payload = new UpdateIdentityPayload(newAlias);
        var userSignature = new SignatureInfo(TestAddress, "user-signature");
        var validatorSignature = new SignatureInfo("validator-address", "validator-signature");

        var unsignedTx = new UnsignedTransaction<UpdateIdentityPayload>(
            new TransactionId(Guid.NewGuid()),
            UpdateIdentityPayloadHandler.UpdateIdentityPayloadKind,
            Timestamp.Current,
            payload,
            1000);

        var signedTx = new SignedTransaction<UpdateIdentityPayload>(unsignedTx, userSignature);
        return new ValidatedTransaction<UpdateIdentityPayload>(signedTx, validatorSignature);
    }

    #endregion
}
