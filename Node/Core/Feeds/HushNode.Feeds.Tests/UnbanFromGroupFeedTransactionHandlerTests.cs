using FluentAssertions;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Feeds.Model;
using Moq;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for UnbanFromGroupFeedTransactionHandler - verifies participant status update
/// and key rotation trigger with correct parameters.
/// Unban operations are cryptographic - they include unbanned members in new key distribution.
/// NOTE: Unbanned members cannot read messages from their ban period (security by design).
/// </summary>
public class UnbanFromGroupFeedTransactionHandlerTests
{
    #region Participant Status Update Tests

    [Fact]
    public async Task HandleUnbanFromGroupFeedTransactionAsync_UpdatesParticipantToMember()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForAdminControls(mocker);

        var handler = mocker.CreateInstance<UnbanFromGroupFeedTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var unbannedAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateUnbanFromGroupFeedPayload(feedId, adminAddress, unbannedAddress);
        var transaction = TestDataFactory.CreateUnbanFromGroupFeedValidatedTransaction(payload, adminAddress);

        // Act
        await handler.HandleUnbanFromGroupFeedTransactionAsync(transaction);

        // Assert
        mocker.GetMock<IFeedsStorageService>()
            .Verify(x => x.UpdateParticipantTypeAsync(
                feedId,
                unbannedAddress,
                ParticipantType.Member), Times.Once);
    }

    [Fact]
    public async Task HandleUnbanFromGroupFeedTransactionAsync_CallsUpdateParticipantTypeWithCorrectFeedId()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForAdminControls(mocker);

        var handler = mocker.CreateInstance<UnbanFromGroupFeedTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var unbannedAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateUnbanFromGroupFeedPayload(feedId, adminAddress, unbannedAddress);
        var transaction = TestDataFactory.CreateUnbanFromGroupFeedValidatedTransaction(payload, adminAddress);

        FeedId? capturedFeedId = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.UpdateParticipantTypeAsync(It.IsAny<FeedId>(), It.IsAny<string>(), It.IsAny<ParticipantType>()))
            .Callback<FeedId, string, ParticipantType>((fid, _, _) => capturedFeedId = fid)
            .Returns(Task.CompletedTask);

        // Act
        await handler.HandleUnbanFromGroupFeedTransactionAsync(transaction);

        // Assert
        capturedFeedId.Should().Be(feedId);
    }

    #endregion

    #region Key Rotation Trigger Tests

    [Fact]
    public async Task HandleUnbanFromGroupFeedTransactionAsync_TriggersKeyRotation()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForAdminControls(mocker);

        var handler = mocker.CreateInstance<UnbanFromGroupFeedTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var unbannedAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateUnbanFromGroupFeedPayload(feedId, adminAddress, unbannedAddress);
        var transaction = TestDataFactory.CreateUnbanFromGroupFeedValidatedTransaction(payload, adminAddress);

        // Act
        await handler.HandleUnbanFromGroupFeedTransactionAsync(transaction);

        // Assert
        mocker.GetMock<IKeyRotationService>()
            .Verify(x => x.TriggerRotationAsync(
                feedId,
                RotationTrigger.Unban,
                unbannedAddress,
                null), Times.Once);
    }

    [Fact]
    public async Task HandleUnbanFromGroupFeedTransactionAsync_UsesCorrectRotationTrigger()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForAdminControls(mocker);

        var handler = mocker.CreateInstance<UnbanFromGroupFeedTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var unbannedAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateUnbanFromGroupFeedPayload(feedId, adminAddress, unbannedAddress);
        var transaction = TestDataFactory.CreateUnbanFromGroupFeedValidatedTransaction(payload, adminAddress);

        RotationTrigger? capturedTrigger = null;
        mocker.GetMock<IKeyRotationService>()
            .Setup(x => x.TriggerRotationAsync(It.IsAny<FeedId>(), It.IsAny<RotationTrigger>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<FeedId, RotationTrigger, string?, string?>((_, trigger, _, _) => capturedTrigger = trigger)
            .ReturnsAsync(KeyRotationResult.Failure("Mock result"));

        // Act
        await handler.HandleUnbanFromGroupFeedTransactionAsync(transaction);

        // Assert
        capturedTrigger.Should().Be(RotationTrigger.Unban);
    }

    [Fact]
    public async Task HandleUnbanFromGroupFeedTransactionAsync_PassesUnbannedMemberAsJoiningAddress()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForAdminControls(mocker);

        var handler = mocker.CreateInstance<UnbanFromGroupFeedTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var unbannedAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateUnbanFromGroupFeedPayload(feedId, adminAddress, unbannedAddress);
        var transaction = TestDataFactory.CreateUnbanFromGroupFeedValidatedTransaction(payload, adminAddress);

        string? capturedLeavingAddress = null;
        string? capturedJoiningAddress = null;
        mocker.GetMock<IKeyRotationService>()
            .Setup(x => x.TriggerRotationAsync(It.IsAny<FeedId>(), It.IsAny<RotationTrigger>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<FeedId, RotationTrigger, string?, string?>((_, _, joining, leaving) =>
            {
                capturedJoiningAddress = joining;
                capturedLeavingAddress = leaving;
            })
            .ReturnsAsync(KeyRotationResult.Failure("Mock result"));

        // Act
        await handler.HandleUnbanFromGroupFeedTransactionAsync(transaction);

        // Assert
        capturedJoiningAddress.Should().Be(unbannedAddress, "Unbanned member should be the joining member");
        capturedLeavingAddress.Should().BeNull("Unban should not have a leaving member");
    }

    #endregion

    #region Operation Order Tests

    [Fact]
    public async Task HandleUnbanFromGroupFeedTransactionAsync_UpdatesStatusBeforeKeyRotation()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForAdminControls(mocker);

        var handler = mocker.CreateInstance<UnbanFromGroupFeedTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var unbannedAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateUnbanFromGroupFeedPayload(feedId, adminAddress, unbannedAddress);
        var transaction = TestDataFactory.CreateUnbanFromGroupFeedValidatedTransaction(payload, adminAddress);

        var callOrder = new List<string>();
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.UpdateParticipantTypeAsync(It.IsAny<FeedId>(), It.IsAny<string>(), It.IsAny<ParticipantType>()))
            .Callback(() => callOrder.Add("UpdateParticipantType"))
            .Returns(Task.CompletedTask);

        mocker.GetMock<IKeyRotationService>()
            .Setup(x => x.TriggerRotationAsync(It.IsAny<FeedId>(), It.IsAny<RotationTrigger>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback(() => callOrder.Add("TriggerRotation"))
            .ReturnsAsync(KeyRotationResult.Failure("Mock result"));

        // Act
        await handler.HandleUnbanFromGroupFeedTransactionAsync(transaction);

        // Assert
        callOrder.Should().HaveCount(2);
        callOrder[0].Should().Be("UpdateParticipantType");
        callOrder[1].Should().Be("TriggerRotation");
    }

    #endregion

    #region Ban vs Unban Symmetry Tests

    [Fact]
    public async Task HandleUnbanFromGroupFeedTransactionAsync_SymmetricToBan_OppositeJoiningLeavingParameters()
    {
        // This test verifies the key difference between Ban and Unban:
        // - Ban: leavingMemberAddress = banned member, joiningMemberAddress = null
        // - Unban: joiningMemberAddress = unbanned member, leavingMemberAddress = null

        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureFeedsStorageForAdminControls(mocker);

        var handler = mocker.CreateInstance<UnbanFromGroupFeedTransactionHandler>();
        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var memberAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateUnbanFromGroupFeedPayload(feedId, adminAddress, memberAddress);
        var transaction = TestDataFactory.CreateUnbanFromGroupFeedValidatedTransaction(payload, adminAddress);

        string? joiningAddress = null;
        string? leavingAddress = null;
        mocker.GetMock<IKeyRotationService>()
            .Setup(x => x.TriggerRotationAsync(It.IsAny<FeedId>(), It.IsAny<RotationTrigger>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<FeedId, RotationTrigger, string?, string?>((_, _, j, l) =>
            {
                joiningAddress = j;
                leavingAddress = l;
            })
            .ReturnsAsync(KeyRotationResult.Failure("Mock result"));

        // Act
        await handler.HandleUnbanFromGroupFeedTransactionAsync(transaction);

        // Assert - Unban includes member (joining), doesn't exclude anyone (leaving = null)
        joiningAddress.Should().Be(memberAddress);
        leavingAddress.Should().BeNull();
    }

    #endregion
}
