using FluentAssertions;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Feeds.Model;
using Moq;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for BanFromGroupFeedTransactionHandler - verifies participant status update
/// and key rotation trigger with correct parameters.
/// Ban operations are cryptographic - they exclude banned members from key distribution.
/// </summary>
public class BanFromGroupFeedTransactionHandlerTests
{
    #region Participant Status Update Tests

    [Fact]
    public async Task HandleBanFromGroupFeedTransactionAsync_UpdatesParticipantToBanned()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForAdminControls(mocker);

        var handler = mocker.CreateInstance<BanFromGroupFeedTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var bannedAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateBanFromGroupFeedPayload(feedId, adminAddress, bannedAddress);
        var transaction = TestDataFactory.CreateBanFromGroupFeedValidatedTransaction(payload, adminAddress);

        // Act
        await handler.HandleBanFromGroupFeedTransactionAsync(transaction);

        // Assert
        mocker.GetMock<IFeedsStorageService>()
            .Verify(x => x.UpdateParticipantTypeAsync(
                feedId,
                bannedAddress,
                ParticipantType.Banned), Times.Once);
    }

    [Fact]
    public async Task HandleBanFromGroupFeedTransactionAsync_CallsUpdateParticipantTypeWithCorrectFeedId()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForAdminControls(mocker);

        var handler = mocker.CreateInstance<BanFromGroupFeedTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var bannedAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateBanFromGroupFeedPayload(feedId, adminAddress, bannedAddress);
        var transaction = TestDataFactory.CreateBanFromGroupFeedValidatedTransaction(payload, adminAddress);

        FeedId? capturedFeedId = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.UpdateParticipantTypeAsync(It.IsAny<FeedId>(), It.IsAny<string>(), It.IsAny<ParticipantType>()))
            .Callback<FeedId, string, ParticipantType>((fid, _, _) => capturedFeedId = fid)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleBanFromGroupFeedTransactionAsync(transaction);

        // Assert
        capturedFeedId.Should().Be(feedId);
    }

    #endregion

    #region Key Rotation Trigger Tests

    [Fact]
    public async Task HandleBanFromGroupFeedTransactionAsync_TriggersKeyRotation()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForAdminControls(mocker);

        var handler = mocker.CreateInstance<BanFromGroupFeedTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var bannedAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateBanFromGroupFeedPayload(feedId, adminAddress, bannedAddress);
        var transaction = TestDataFactory.CreateBanFromGroupFeedValidatedTransaction(payload, adminAddress);

        // Act
        await handler.HandleBanFromGroupFeedTransactionAsync(transaction);

        // Assert
        mocker.GetMock<IKeyRotationService>()
            .Verify(x => x.TriggerAndPersistRotationAsync(
                feedId,
                RotationTrigger.Ban,
                null,
                bannedAddress), Times.Once);
    }

    [Fact]
    public async Task HandleBanFromGroupFeedTransactionAsync_UsesCorrectRotationTrigger()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForAdminControls(mocker);

        var handler = mocker.CreateInstance<BanFromGroupFeedTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var bannedAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateBanFromGroupFeedPayload(feedId, adminAddress, bannedAddress);
        var transaction = TestDataFactory.CreateBanFromGroupFeedValidatedTransaction(payload, adminAddress);

        RotationTrigger? capturedTrigger = null;
        mocker.GetMock<IKeyRotationService>()
            .Setup(x => x.TriggerAndPersistRotationAsync(It.IsAny<FeedId>(), It.IsAny<RotationTrigger>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<FeedId, RotationTrigger, string?, string?>((_, trigger, _, _) => capturedTrigger = trigger)
            .ReturnsAsync(KeyRotationResult.Failure("Mock result"));

        // Act
        await handler.HandleBanFromGroupFeedTransactionAsync(transaction);

        // Assert
        capturedTrigger.Should().Be(RotationTrigger.Ban);
    }

    [Fact]
    public async Task HandleBanFromGroupFeedTransactionAsync_PassesBannedMemberAsLeavingAddress()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForAdminControls(mocker);

        var handler = mocker.CreateInstance<BanFromGroupFeedTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var bannedAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateBanFromGroupFeedPayload(feedId, adminAddress, bannedAddress);
        var transaction = TestDataFactory.CreateBanFromGroupFeedValidatedTransaction(payload, adminAddress);

        string? capturedLeavingAddress = null;
        string? capturedJoiningAddress = null;
        mocker.GetMock<IKeyRotationService>()
            .Setup(x => x.TriggerAndPersistRotationAsync(It.IsAny<FeedId>(), It.IsAny<RotationTrigger>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<FeedId, RotationTrigger, string?, string?>((_, _, joining, leaving) =>
            {
                capturedJoiningAddress = joining;
                capturedLeavingAddress = leaving;
            })
            .ReturnsAsync(KeyRotationResult.Failure("Mock result"));

        // Act
        await handler.HandleBanFromGroupFeedTransactionAsync(transaction);

        // Assert
        capturedJoiningAddress.Should().BeNull("Ban should not have a joining member");
        capturedLeavingAddress.Should().Be(bannedAddress, "Banned member should be the leaving member");
    }

    #endregion

    #region Operation Order Tests

    [Fact]
    public async Task HandleBanFromGroupFeedTransactionAsync_UpdatesStatusBeforeKeyRotation()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForAdminControls(mocker);

        var handler = mocker.CreateInstance<BanFromGroupFeedTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var bannedAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateBanFromGroupFeedPayload(feedId, adminAddress, bannedAddress);
        var transaction = TestDataFactory.CreateBanFromGroupFeedValidatedTransaction(payload, adminAddress);

        var callOrder = new List<string>();
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.UpdateParticipantTypeAsync(It.IsAny<FeedId>(), It.IsAny<string>(), It.IsAny<ParticipantType>()))
            .Callback(() => callOrder.Add("UpdateParticipantType"))
            .Returns(Task.CompletedTask);

        mocker.GetMock<IKeyRotationService>()
            .Setup(x => x.TriggerAndPersistRotationAsync(It.IsAny<FeedId>(), It.IsAny<RotationTrigger>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback(() => callOrder.Add("TriggerAndPersistRotation"))
            .ReturnsAsync(KeyRotationResult.Failure("Mock result"));

        // Act
        await handler.HandleBanFromGroupFeedTransactionAsync(transaction);

        // Assert
        callOrder.Should().HaveCount(2);
        callOrder[0].Should().Be("UpdateParticipantType");
        callOrder[1].Should().Be("TriggerAndPersistRotation");
    }

    #endregion
}
