using FluentAssertions;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Moq;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for GroupFeedKeyRotationTransactionHandler - entity creation and storage.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class GroupFeedKeyRotationTransactionHandlerTests
{
    #region KeyGeneration Entity Tests

    [Fact]
    public async Task HandleKeyRotationTransactionAsync_CreatesKeyGenerationEntityWithCorrectProperties()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForKeyRotation(mocker);

        var handler = mocker.CreateInstance<GroupFeedKeyRotationTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var payload = TestDataFactory.CreateKeyRotationPayload(
            feedId,
            newKeyGeneration: 3,
            previousKeyGeneration: 2,
            validFromBlock: 500,
            memberCount: 2);
        var transaction = TestDataFactory.CreateKeyRotationValidatedTransaction(payload, TestDataFactory.CreateAddress());

        GroupFeedKeyGenerationEntity? capturedEntity = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateKeyRotationAsync(It.IsAny<GroupFeedKeyGenerationEntity>()))
            .Callback<GroupFeedKeyGenerationEntity>(e => capturedEntity = e)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleKeyRotationTransactionAsync(transaction);

        // Assert
        capturedEntity.Should().NotBeNull();
        capturedEntity!.FeedId.Should().Be(feedId);
        capturedEntity.KeyGeneration.Should().Be(3);
        capturedEntity.ValidFromBlock.Should().Be(new BlockIndex(500));
        capturedEntity.RotationTrigger.Should().Be(RotationTrigger.Join);
    }

    [Fact]
    public async Task HandleKeyRotationTransactionAsync_PreservesRotationTrigger()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForKeyRotation(mocker);

        var handler = mocker.CreateInstance<GroupFeedKeyRotationTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var payload = new GroupFeedKeyRotationPayload(
            feedId,
            NewKeyGeneration: 2,
            PreviousKeyGeneration: 1,
            ValidFromBlock: 100,
            EncryptedKeys: new[]
            {
                new GroupFeedEncryptedKey(TestDataFactory.CreateAddress(), Convert.ToBase64String(new byte[128]))
            },
            RotationTrigger: RotationTrigger.Leave);
        var transaction = TestDataFactory.CreateKeyRotationValidatedTransaction(payload, TestDataFactory.CreateAddress());

        GroupFeedKeyGenerationEntity? capturedEntity = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateKeyRotationAsync(It.IsAny<GroupFeedKeyGenerationEntity>()))
            .Callback<GroupFeedKeyGenerationEntity>(e => capturedEntity = e)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleKeyRotationTransactionAsync(transaction);

        // Assert
        capturedEntity.Should().NotBeNull();
        capturedEntity!.RotationTrigger.Should().Be(RotationTrigger.Leave);
    }

    [Theory]
    [InlineData(RotationTrigger.Join)]
    [InlineData(RotationTrigger.Leave)]
    [InlineData(RotationTrigger.Ban)]
    [InlineData(RotationTrigger.Unban)]
    [InlineData(RotationTrigger.Manual)]
    public async Task HandleKeyRotationTransactionAsync_HandlesAllRotationTriggers(RotationTrigger trigger)
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForKeyRotation(mocker);

        var handler = mocker.CreateInstance<GroupFeedKeyRotationTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var payload = new GroupFeedKeyRotationPayload(
            feedId,
            NewKeyGeneration: 2,
            PreviousKeyGeneration: 1,
            ValidFromBlock: 100,
            EncryptedKeys: new[]
            {
                new GroupFeedEncryptedKey(TestDataFactory.CreateAddress(), Convert.ToBase64String(new byte[128]))
            },
            RotationTrigger: trigger);
        var transaction = TestDataFactory.CreateKeyRotationValidatedTransaction(payload, TestDataFactory.CreateAddress());

        GroupFeedKeyGenerationEntity? capturedEntity = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateKeyRotationAsync(It.IsAny<GroupFeedKeyGenerationEntity>()))
            .Callback<GroupFeedKeyGenerationEntity>(e => capturedEntity = e)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleKeyRotationTransactionAsync(transaction);

        // Assert
        capturedEntity.Should().NotBeNull();
        capturedEntity!.RotationTrigger.Should().Be(trigger);
    }

    #endregion

    #region Encrypted Keys Tests

    [Fact]
    public async Task HandleKeyRotationTransactionAsync_CreatesEncryptedKeysForAllMembers()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForKeyRotation(mocker);

        var handler = mocker.CreateInstance<GroupFeedKeyRotationTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var payload = TestDataFactory.CreateKeyRotationPayload(feedId, memberCount: 5);
        var transaction = TestDataFactory.CreateKeyRotationValidatedTransaction(payload, TestDataFactory.CreateAddress());

        GroupFeedKeyGenerationEntity? capturedEntity = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateKeyRotationAsync(It.IsAny<GroupFeedKeyGenerationEntity>()))
            .Callback<GroupFeedKeyGenerationEntity>(e => capturedEntity = e)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleKeyRotationTransactionAsync(transaction);

        // Assert
        capturedEntity.Should().NotBeNull();
        capturedEntity!.EncryptedKeys.Should().HaveCount(5);
    }

    [Fact]
    public async Task HandleKeyRotationTransactionAsync_EncryptedKeysLinkedToCorrectKeyGeneration()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForKeyRotation(mocker);

        var handler = mocker.CreateInstance<GroupFeedKeyRotationTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var payload = TestDataFactory.CreateKeyRotationPayload(feedId, newKeyGeneration: 7, previousKeyGeneration: 6, memberCount: 3);
        var transaction = TestDataFactory.CreateKeyRotationValidatedTransaction(payload, TestDataFactory.CreateAddress());

        GroupFeedKeyGenerationEntity? capturedEntity = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateKeyRotationAsync(It.IsAny<GroupFeedKeyGenerationEntity>()))
            .Callback<GroupFeedKeyGenerationEntity>(e => capturedEntity = e)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleKeyRotationTransactionAsync(transaction);

        // Assert
        capturedEntity.Should().NotBeNull();
        capturedEntity!.EncryptedKeys.Should().AllSatisfy(ek =>
            ek.KeyGeneration.Should().Be(7));
    }

    [Fact]
    public async Task HandleKeyRotationTransactionAsync_EncryptedKeysHaveCorrectFeedId()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForKeyRotation(mocker);

        var handler = mocker.CreateInstance<GroupFeedKeyRotationTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var payload = TestDataFactory.CreateKeyRotationPayload(feedId, memberCount: 2);
        var transaction = TestDataFactory.CreateKeyRotationValidatedTransaction(payload, TestDataFactory.CreateAddress());

        GroupFeedKeyGenerationEntity? capturedEntity = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateKeyRotationAsync(It.IsAny<GroupFeedKeyGenerationEntity>()))
            .Callback<GroupFeedKeyGenerationEntity>(e => capturedEntity = e)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleKeyRotationTransactionAsync(transaction);

        // Assert
        capturedEntity.Should().NotBeNull();
        capturedEntity!.EncryptedKeys.Should().AllSatisfy(ek =>
            ek.FeedId.Should().Be(feedId));
    }

    [Fact]
    public async Task HandleKeyRotationTransactionAsync_EncryptedKeysHaveCorrectMemberData()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForKeyRotation(mocker);

        var handler = mocker.CreateInstance<GroupFeedKeyRotationTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var member1Address = TestDataFactory.CreateAddress();
        var member2Address = TestDataFactory.CreateAddress();
        var member1Key = Convert.ToBase64String(new byte[128]);
        var member2Key = Convert.ToBase64String(new byte[128]);

        var payload = new GroupFeedKeyRotationPayload(
            feedId,
            NewKeyGeneration: 2,
            PreviousKeyGeneration: 1,
            ValidFromBlock: 100,
            EncryptedKeys: new[]
            {
                new GroupFeedEncryptedKey(member1Address, member1Key),
                new GroupFeedEncryptedKey(member2Address, member2Key)
            },
            RotationTrigger: RotationTrigger.Join);
        var transaction = TestDataFactory.CreateKeyRotationValidatedTransaction(payload, TestDataFactory.CreateAddress());

        GroupFeedKeyGenerationEntity? capturedEntity = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateKeyRotationAsync(It.IsAny<GroupFeedKeyGenerationEntity>()))
            .Callback<GroupFeedKeyGenerationEntity>(e => capturedEntity = e)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleKeyRotationTransactionAsync(transaction);

        // Assert
        capturedEntity.Should().NotBeNull();

        var encryptedKey1 = capturedEntity!.EncryptedKeys.FirstOrDefault(ek => ek.MemberPublicAddress == member1Address);
        encryptedKey1.Should().NotBeNull();
        encryptedKey1!.EncryptedAesKey.Should().Be(member1Key);

        var encryptedKey2 = capturedEntity.EncryptedKeys.FirstOrDefault(ek => ek.MemberPublicAddress == member2Address);
        encryptedKey2.Should().NotBeNull();
        encryptedKey2!.EncryptedAesKey.Should().Be(member2Key);
    }

    #endregion

    #region Storage Service Tests

    [Fact]
    public async Task HandleKeyRotationTransactionAsync_CallsCreateKeyRotationAsync()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForKeyRotation(mocker);

        var handler = mocker.CreateInstance<GroupFeedKeyRotationTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var payload = TestDataFactory.CreateKeyRotationPayload(feedId);
        var transaction = TestDataFactory.CreateKeyRotationValidatedTransaction(payload, TestDataFactory.CreateAddress());

        // Act
        await handler.HandleKeyRotationTransactionAsync(transaction);

        // Assert
        mocker.GetMock<IFeedsStorageService>()
            .Verify(x => x.CreateKeyRotationAsync(It.IsAny<GroupFeedKeyGenerationEntity>()), Times.Once);
    }

    [Fact]
    public async Task HandleKeyRotationTransactionAsync_CallsStorageServiceWithCorrectEntity()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForKeyRotation(mocker);

        var handler = mocker.CreateInstance<GroupFeedKeyRotationTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var payload = TestDataFactory.CreateKeyRotationPayload(feedId, newKeyGeneration: 10, previousKeyGeneration: 9);
        var transaction = TestDataFactory.CreateKeyRotationValidatedTransaction(payload, TestDataFactory.CreateAddress());

        // Act
        await handler.HandleKeyRotationTransactionAsync(transaction);

        // Assert
        mocker.GetMock<IFeedsStorageService>()
            .Verify(x => x.CreateKeyRotationAsync(It.Is<GroupFeedKeyGenerationEntity>(e =>
                e.FeedId == feedId &&
                e.KeyGeneration == 10)), Times.Once);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task HandleKeyRotationTransactionAsync_SingleMember_CreatesOneEncryptedKey()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForKeyRotation(mocker);

        var handler = mocker.CreateInstance<GroupFeedKeyRotationTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var payload = TestDataFactory.CreateKeyRotationPayload(feedId, memberCount: 1);
        var transaction = TestDataFactory.CreateKeyRotationValidatedTransaction(payload, TestDataFactory.CreateAddress());

        GroupFeedKeyGenerationEntity? capturedEntity = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateKeyRotationAsync(It.IsAny<GroupFeedKeyGenerationEntity>()))
            .Callback<GroupFeedKeyGenerationEntity>(e => capturedEntity = e)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleKeyRotationTransactionAsync(transaction);

        // Assert
        capturedEntity.Should().NotBeNull();
        capturedEntity!.EncryptedKeys.Should().HaveCount(1);
    }

    [Fact]
    public async Task HandleKeyRotationTransactionAsync_FirstRotation_KeyGeneration1()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForKeyRotation(mocker);

        var handler = mocker.CreateInstance<GroupFeedKeyRotationTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var payload = TestDataFactory.CreateKeyRotationPayload(feedId, newKeyGeneration: 1, previousKeyGeneration: 0);
        var transaction = TestDataFactory.CreateKeyRotationValidatedTransaction(payload, TestDataFactory.CreateAddress());

        GroupFeedKeyGenerationEntity? capturedEntity = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateKeyRotationAsync(It.IsAny<GroupFeedKeyGenerationEntity>()))
            .Callback<GroupFeedKeyGenerationEntity>(e => capturedEntity = e)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleKeyRotationTransactionAsync(transaction);

        // Assert
        capturedEntity.Should().NotBeNull();
        capturedEntity!.KeyGeneration.Should().Be(1);
    }

    [Fact]
    public async Task HandleKeyRotationTransactionAsync_LargeKeyGeneration_HandlesCorrectly()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForKeyRotation(mocker);

        var handler = mocker.CreateInstance<GroupFeedKeyRotationTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var payload = TestDataFactory.CreateKeyRotationPayload(feedId, newKeyGeneration: 999, previousKeyGeneration: 998);
        var transaction = TestDataFactory.CreateKeyRotationValidatedTransaction(payload, TestDataFactory.CreateAddress());

        GroupFeedKeyGenerationEntity? capturedEntity = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateKeyRotationAsync(It.IsAny<GroupFeedKeyGenerationEntity>()))
            .Callback<GroupFeedKeyGenerationEntity>(e => capturedEntity = e)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleKeyRotationTransactionAsync(transaction);

        // Assert
        capturedEntity.Should().NotBeNull();
        capturedEntity!.KeyGeneration.Should().Be(999);
    }

    #endregion
}
