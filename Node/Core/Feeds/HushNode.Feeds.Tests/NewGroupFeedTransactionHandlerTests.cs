using FluentAssertions;
using HushNode.Caching;
using HushNode.Feeds.Events;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Feeds.Model;
using Moq;
using Moq.AutoMock;
using Olimpo;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for NewGroupFeedTransactionHandler - entity creation and storage.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class NewGroupFeedTransactionHandlerTests
{
    private const long CurrentBlockIndex = 100;

    #region Role Assignment Tests

    [Fact]
    public async Task HandleNewGroupFeedTransactionAsync_CreatorGetsAdminRole()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageService(mocker);
        MockServices.ConfigureBlockchainCache(mocker, CurrentBlockIndex);
        MockServices.ConfigureEventAggregator(mocker);

        var handler = mocker.CreateInstance<NewGroupFeedTransactionHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateValidPayload(creatorAddress, participantCount: 3);
        var transaction = TestDataFactory.CreateValidatedTransaction(payload, creatorAddress);

        GroupFeed? capturedGroupFeed = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateGroupFeed(It.IsAny<GroupFeed>()))
            .Callback<GroupFeed>(gf => capturedGroupFeed = gf)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleNewGroupFeedTransactionAsync(transaction);

        // Assert
        capturedGroupFeed.Should().NotBeNull();
        var creatorParticipant = capturedGroupFeed!.Participants
            .FirstOrDefault(p => p.ParticipantPublicAddress == creatorAddress);

        creatorParticipant.Should().NotBeNull();
        creatorParticipant!.ParticipantType.Should().Be(ParticipantType.Admin);
    }

    [Fact]
    public async Task HandleNewGroupFeedTransactionAsync_NonCreatorGetsMemberRole()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageService(mocker);
        MockServices.ConfigureBlockchainCache(mocker, CurrentBlockIndex);
        MockServices.ConfigureEventAggregator(mocker);

        var handler = mocker.CreateInstance<NewGroupFeedTransactionHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateValidPayload(creatorAddress, participantCount: 3);
        var transaction = TestDataFactory.CreateValidatedTransaction(payload, creatorAddress);

        GroupFeed? capturedGroupFeed = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateGroupFeed(It.IsAny<GroupFeed>()))
            .Callback<GroupFeed>(gf => capturedGroupFeed = gf)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleNewGroupFeedTransactionAsync(transaction);

        // Assert
        capturedGroupFeed.Should().NotBeNull();
        var nonCreatorParticipants = capturedGroupFeed!.Participants
            .Where(p => p.ParticipantPublicAddress != creatorAddress)
            .ToList();

        nonCreatorParticipants.Should().HaveCount(2);
        nonCreatorParticipants.Should().AllSatisfy(p =>
            p.ParticipantType.Should().Be(ParticipantType.Member));
    }

    [Fact]
    public async Task HandleNewGroupFeedTransactionAsync_SingleParticipant_CreatorIsAdmin()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageService(mocker);
        MockServices.ConfigureBlockchainCache(mocker, CurrentBlockIndex);
        MockServices.ConfigureEventAggregator(mocker);

        var handler = mocker.CreateInstance<NewGroupFeedTransactionHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateValidPayload(creatorAddress, participantCount: 1);
        var transaction = TestDataFactory.CreateValidatedTransaction(payload, creatorAddress);

        GroupFeed? capturedGroupFeed = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateGroupFeed(It.IsAny<GroupFeed>()))
            .Callback<GroupFeed>(gf => capturedGroupFeed = gf)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleNewGroupFeedTransactionAsync(transaction);

        // Assert
        capturedGroupFeed.Should().NotBeNull();
        capturedGroupFeed!.Participants.Should().HaveCount(1);
        capturedGroupFeed.Participants.First().ParticipantType.Should().Be(ParticipantType.Admin);
    }

    #endregion

    #region KeyGeneration 0 Tests

    [Fact]
    public async Task HandleNewGroupFeedTransactionAsync_CreatesKeyGeneration0()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageService(mocker);
        MockServices.ConfigureBlockchainCache(mocker, CurrentBlockIndex);
        MockServices.ConfigureEventAggregator(mocker);

        var handler = mocker.CreateInstance<NewGroupFeedTransactionHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateValidPayload(creatorAddress, participantCount: 2);
        var transaction = TestDataFactory.CreateValidatedTransaction(payload, creatorAddress);

        GroupFeed? capturedGroupFeed = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateGroupFeed(It.IsAny<GroupFeed>()))
            .Callback<GroupFeed>(gf => capturedGroupFeed = gf)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleNewGroupFeedTransactionAsync(transaction);

        // Assert
        capturedGroupFeed.Should().NotBeNull();
        capturedGroupFeed!.KeyGenerations.Should().HaveCount(1);

        var keyGeneration = capturedGroupFeed.KeyGenerations.First();
        keyGeneration.KeyGeneration.Should().Be(0);
        keyGeneration.ValidFromBlock.Value.Should().Be(CurrentBlockIndex);
        keyGeneration.RotationTrigger.Should().Be(RotationTrigger.Join);
    }

    [Fact]
    public async Task HandleNewGroupFeedTransactionAsync_KeyGeneration0HasCorrectFeedId()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageService(mocker);
        MockServices.ConfigureBlockchainCache(mocker, CurrentBlockIndex);
        MockServices.ConfigureEventAggregator(mocker);

        var handler = mocker.CreateInstance<NewGroupFeedTransactionHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateValidPayload(creatorAddress);
        var transaction = TestDataFactory.CreateValidatedTransaction(payload, creatorAddress);

        GroupFeed? capturedGroupFeed = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateGroupFeed(It.IsAny<GroupFeed>()))
            .Callback<GroupFeed>(gf => capturedGroupFeed = gf)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleNewGroupFeedTransactionAsync(transaction);

        // Assert
        var keyGeneration = capturedGroupFeed!.KeyGenerations.First();
        keyGeneration.FeedId.Should().Be(payload.FeedId);
    }

    #endregion

    #region Encrypted Keys Tests

    [Fact]
    public async Task HandleNewGroupFeedTransactionAsync_CreatesEncryptedKeysForAllParticipants()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageService(mocker);
        MockServices.ConfigureBlockchainCache(mocker, CurrentBlockIndex);
        MockServices.ConfigureEventAggregator(mocker);

        var handler = mocker.CreateInstance<NewGroupFeedTransactionHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateValidPayload(creatorAddress, participantCount: 3);
        var transaction = TestDataFactory.CreateValidatedTransaction(payload, creatorAddress);

        GroupFeed? capturedGroupFeed = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateGroupFeed(It.IsAny<GroupFeed>()))
            .Callback<GroupFeed>(gf => capturedGroupFeed = gf)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleNewGroupFeedTransactionAsync(transaction);

        // Assert
        var keyGeneration = capturedGroupFeed!.KeyGenerations.First();
        keyGeneration.EncryptedKeys.Should().HaveCount(3);
    }

    [Fact]
    public async Task HandleNewGroupFeedTransactionAsync_EncryptedKeysLinkedToKeyGeneration0()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageService(mocker);
        MockServices.ConfigureBlockchainCache(mocker, CurrentBlockIndex);
        MockServices.ConfigureEventAggregator(mocker);

        var handler = mocker.CreateInstance<NewGroupFeedTransactionHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateValidPayload(creatorAddress, participantCount: 2);
        var transaction = TestDataFactory.CreateValidatedTransaction(payload, creatorAddress);

        GroupFeed? capturedGroupFeed = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateGroupFeed(It.IsAny<GroupFeed>()))
            .Callback<GroupFeed>(gf => capturedGroupFeed = gf)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleNewGroupFeedTransactionAsync(transaction);

        // Assert
        var keyGeneration = capturedGroupFeed!.KeyGenerations.First();
        keyGeneration.EncryptedKeys.Should().AllSatisfy(ek =>
            ek.KeyGeneration.Should().Be(0));
    }

    [Fact]
    public async Task HandleNewGroupFeedTransactionAsync_EncryptedKeysHaveCorrectData()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageService(mocker);
        MockServices.ConfigureBlockchainCache(mocker, CurrentBlockIndex);
        MockServices.ConfigureEventAggregator(mocker);

        var handler = mocker.CreateInstance<NewGroupFeedTransactionHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateValidPayload(creatorAddress, participantCount: 2);
        var transaction = TestDataFactory.CreateValidatedTransaction(payload, creatorAddress);

        GroupFeed? capturedGroupFeed = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateGroupFeed(It.IsAny<GroupFeed>()))
            .Callback<GroupFeed>(gf => capturedGroupFeed = gf)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleNewGroupFeedTransactionAsync(transaction);

        // Assert
        var keyGeneration = capturedGroupFeed!.KeyGenerations.First();

        // Verify each participant has an encrypted key with their address
        foreach (var participant in payload.Participants)
        {
            var encryptedKey = keyGeneration.EncryptedKeys
                .FirstOrDefault(ek => ek.MemberPublicAddress == participant.ParticipantPublicAddress);

            encryptedKey.Should().NotBeNull();
            encryptedKey!.EncryptedAesKey.Should().Be(participant.EncryptedFeedKey);
        }
    }

    #endregion

    #region GroupFeed Entity Tests

    [Fact]
    public async Task HandleNewGroupFeedTransactionAsync_CreatesGroupFeedWithCorrectProperties()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageService(mocker);
        MockServices.ConfigureBlockchainCache(mocker, CurrentBlockIndex);
        MockServices.ConfigureEventAggregator(mocker);

        var handler = mocker.CreateInstance<NewGroupFeedTransactionHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateValidPayload(
            creatorAddress,
            participantCount: 2,
            title: "My Test Group",
            description: "A test description",
            isPublic: true);
        var transaction = TestDataFactory.CreateValidatedTransaction(payload, creatorAddress);

        GroupFeed? capturedGroupFeed = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateGroupFeed(It.IsAny<GroupFeed>()))
            .Callback<GroupFeed>(gf => capturedGroupFeed = gf)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleNewGroupFeedTransactionAsync(transaction);

        // Assert
        capturedGroupFeed.Should().NotBeNull();
        capturedGroupFeed!.FeedId.Should().Be(payload.FeedId);
        capturedGroupFeed.Title.Should().Be("My Test Group");
        capturedGroupFeed.Description.Should().Be("A test description");
        capturedGroupFeed.IsPublic.Should().BeTrue();
        capturedGroupFeed.CreatedAtBlock.Value.Should().Be(CurrentBlockIndex);
        capturedGroupFeed.CurrentKeyGeneration.Should().Be(0);
    }

    #endregion

    #region Storage Service Tests

    [Fact]
    public async Task HandleNewGroupFeedTransactionAsync_CallsCreateGroupFeed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageService(mocker);
        MockServices.ConfigureBlockchainCache(mocker, CurrentBlockIndex);
        MockServices.ConfigureEventAggregator(mocker);

        var handler = mocker.CreateInstance<NewGroupFeedTransactionHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateValidPayload(creatorAddress);
        var transaction = TestDataFactory.CreateValidatedTransaction(payload, creatorAddress);

        // Act
        await handler.HandleNewGroupFeedTransactionAsync(transaction);

        // Assert
        mocker.GetMock<IFeedsStorageService>()
            .Verify(x => x.CreateGroupFeed(It.IsAny<GroupFeed>()), Times.Once);
    }

    #endregion

    #region Event Publishing Tests

    [Fact]
    public async Task HandleNewGroupFeedTransactionAsync_PublishesFeedCreatedEvent()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageService(mocker);
        MockServices.ConfigureBlockchainCache(mocker, CurrentBlockIndex);
        MockServices.ConfigureEventAggregator(mocker);

        var handler = mocker.CreateInstance<NewGroupFeedTransactionHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateValidPayload(creatorAddress, participantCount: 2);
        var transaction = TestDataFactory.CreateValidatedTransaction(payload, creatorAddress);

        // Act
        await handler.HandleNewGroupFeedTransactionAsync(transaction);

        // Assert
        mocker.GetMock<IEventAggregator>()
            .Verify(x => x.PublishAsync(It.IsAny<FeedCreatedEvent>()), Times.Once);
    }

    [Fact]
    public async Task HandleNewGroupFeedTransactionAsync_EventContainsCorrectFeedId()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageService(mocker);
        MockServices.ConfigureBlockchainCache(mocker, CurrentBlockIndex);
        MockServices.ConfigureEventAggregator(mocker);

        var handler = mocker.CreateInstance<NewGroupFeedTransactionHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateValidPayload(creatorAddress);
        var transaction = TestDataFactory.CreateValidatedTransaction(payload, creatorAddress);

        FeedCreatedEvent? capturedEvent = null;
        mocker.GetMock<IEventAggregator>()
            .Setup(x => x.PublishAsync(It.IsAny<FeedCreatedEvent>()))
            .Callback<object>(e => capturedEvent = e as FeedCreatedEvent)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleNewGroupFeedTransactionAsync(transaction);

        // Assert
        capturedEvent.Should().NotBeNull();
        capturedEvent!.FeedId.Should().Be(payload.FeedId);
    }

    [Fact]
    public async Task HandleNewGroupFeedTransactionAsync_EventContainsFeedTypeGroup()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageService(mocker);
        MockServices.ConfigureBlockchainCache(mocker, CurrentBlockIndex);
        MockServices.ConfigureEventAggregator(mocker);

        var handler = mocker.CreateInstance<NewGroupFeedTransactionHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateValidPayload(creatorAddress);
        var transaction = TestDataFactory.CreateValidatedTransaction(payload, creatorAddress);

        FeedCreatedEvent? capturedEvent = null;
        mocker.GetMock<IEventAggregator>()
            .Setup(x => x.PublishAsync(It.IsAny<FeedCreatedEvent>()))
            .Callback<object>(e => capturedEvent = e as FeedCreatedEvent)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleNewGroupFeedTransactionAsync(transaction);

        // Assert
        capturedEvent.Should().NotBeNull();
        capturedEvent!.FeedType.Should().Be(FeedType.Group);
    }

    [Fact]
    public async Task HandleNewGroupFeedTransactionAsync_EventContainsAllParticipantAddresses()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageService(mocker);
        MockServices.ConfigureBlockchainCache(mocker, CurrentBlockIndex);
        MockServices.ConfigureEventAggregator(mocker);

        var handler = mocker.CreateInstance<NewGroupFeedTransactionHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateValidPayload(creatorAddress, participantCount: 3);
        var transaction = TestDataFactory.CreateValidatedTransaction(payload, creatorAddress);

        FeedCreatedEvent? capturedEvent = null;
        mocker.GetMock<IEventAggregator>()
            .Setup(x => x.PublishAsync(It.IsAny<FeedCreatedEvent>()))
            .Callback<object>(e => capturedEvent = e as FeedCreatedEvent)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleNewGroupFeedTransactionAsync(transaction);

        // Assert
        capturedEvent.Should().NotBeNull();
        capturedEvent!.ParticipantPublicAddresses.Should().HaveCount(3);

        var expectedAddresses = payload.Participants.Select(p => p.ParticipantPublicAddress);
        capturedEvent.ParticipantPublicAddresses.Should().BeEquivalentTo(expectedAddresses);
    }

    #endregion
}
