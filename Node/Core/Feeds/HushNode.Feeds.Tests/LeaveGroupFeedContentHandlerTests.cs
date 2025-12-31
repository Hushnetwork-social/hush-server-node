using FluentAssertions;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Feeds.Model;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for LeaveGroupFeedContentHandler - validation and signing of LeaveGroupFeed transactions.
/// </summary>
public class LeaveGroupFeedContentHandlerTests
{
    #region CanValidate Tests

    [Fact]
    public void CanValidate_WithLeaveGroupFeedPayloadKind_ShouldReturnTrue()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<LeaveGroupFeedContentHandler>();

        // Act
        var result = handler.CanValidate(LeaveGroupFeedPayloadHandler.LeaveGroupFeedPayloadKind);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanValidate_WithOtherPayloadKind_ShouldReturnFalse()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<LeaveGroupFeedContentHandler>();

        // Act
        var result = handler.CanValidate(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Member Leaving Tests

    [Fact]
    public void ValidateAndSign_WithMemberLeaving_ShouldSucceed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var memberParticipant = TestDataFactory.CreateParticipantEntity(feedId, memberAddress, ParticipantType.Member);

        MockServices.ConfigureFeedsStorageForLeaveGroup(mocker, groupFeed, memberParticipant, adminCount: 2);

        var handler = mocker.CreateInstance<LeaveGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateLeaveGroupFeedPayload(feedId, memberAddress);
        var transaction = TestDataFactory.CreateLeaveGroupFeedSignedTransaction(payload, memberAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_WithMismatchedSignatory_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = TestDataFactory.CreateAddress();
        var differentAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var memberParticipant = TestDataFactory.CreateParticipantEntity(feedId, memberAddress, ParticipantType.Member);

        MockServices.ConfigureFeedsStorageForLeaveGroup(mocker, groupFeed, memberParticipant);

        var handler = mocker.CreateInstance<LeaveGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateLeaveGroupFeedPayload(feedId, memberAddress);
        // Sign with different address
        var transaction = TestDataFactory.CreateLeaveGroupFeedSignedTransaction(payload, differentAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Group Validation Tests

    [Fact]
    public void ValidateAndSign_WithNonExistentGroup_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        MockServices.ConfigureFeedsStorageForLeaveGroup(mocker, null);

        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = TestDataFactory.CreateAddress();

        var handler = mocker.CreateInstance<LeaveGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateLeaveGroupFeedPayload(feedId, memberAddress);
        var transaction = TestDataFactory.CreateLeaveGroupFeedSignedTransaction(payload, memberAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithDeletedGroup_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = TestDataFactory.CreateAddress();
        var deletedGroup = TestDataFactory.CreateGroupFeed(feedId, isDeleted: true);
        var memberParticipant = TestDataFactory.CreateParticipantEntity(feedId, memberAddress, ParticipantType.Member);

        MockServices.ConfigureFeedsStorageForLeaveGroup(mocker, deletedGroup, memberParticipant);

        var handler = mocker.CreateInstance<LeaveGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateLeaveGroupFeedPayload(feedId, memberAddress);
        var transaction = TestDataFactory.CreateLeaveGroupFeedSignedTransaction(payload, memberAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Participant Validation Tests

    [Fact]
    public void ValidateAndSign_WithNonParticipant_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var nonMemberAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);

        // No participant exists
        MockServices.ConfigureFeedsStorageForLeaveGroup(mocker, groupFeed, null);

        var handler = mocker.CreateInstance<LeaveGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateLeaveGroupFeedPayload(feedId, nonMemberAddress);
        var transaction = TestDataFactory.CreateLeaveGroupFeedSignedTransaction(payload, nonMemberAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithBannedUser_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var bannedAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var bannedParticipant = TestDataFactory.CreateParticipantEntity(feedId, bannedAddress, ParticipantType.Banned);

        MockServices.ConfigureFeedsStorageForLeaveGroup(mocker, groupFeed, bannedParticipant);

        var handler = mocker.CreateInstance<LeaveGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateLeaveGroupFeedPayload(feedId, bannedAddress);
        var transaction = TestDataFactory.CreateLeaveGroupFeedSignedTransaction(payload, bannedAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Admin Leaving Tests

    [Fact]
    public void ValidateAndSign_WithAdminLeavingWhenOtherAdminsExist_ShouldSucceed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);

        // 2 admins exist, so leaving is allowed
        MockServices.ConfigureFeedsStorageForLeaveGroup(mocker, groupFeed, adminParticipant, adminCount: 2);

        var handler = mocker.CreateInstance<LeaveGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateLeaveGroupFeedPayload(feedId, adminAddress);
        var transaction = TestDataFactory.CreateLeaveGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_WithLastAdminLeaving_ShouldSucceed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);

        // Only 1 admin, so leaving will delete the group - but validation still passes
        MockServices.ConfigureFeedsStorageForLeaveGroup(mocker, groupFeed, adminParticipant, adminCount: 1);

        var handler = mocker.CreateInstance<LeaveGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateLeaveGroupFeedPayload(feedId, adminAddress);
        var transaction = TestDataFactory.CreateLeaveGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion
}
