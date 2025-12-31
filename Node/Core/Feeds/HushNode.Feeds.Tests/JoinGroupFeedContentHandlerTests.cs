using FluentAssertions;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for JoinGroupFeedContentHandler - validation and signing of JoinGroupFeed transactions.
/// </summary>
public class JoinGroupFeedContentHandlerTests
{
    #region CanValidate Tests

    [Fact]
    public void CanValidate_WithJoinGroupFeedPayloadKind_ShouldReturnTrue()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<JoinGroupFeedContentHandler>();

        // Act
        var result = handler.CanValidate(JoinGroupFeedPayloadHandler.JoinGroupFeedPayloadKind);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanValidate_WithOtherPayloadKind_ShouldReturnFalse()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<JoinGroupFeedContentHandler>();

        // Act
        var result = handler.CanValidate(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Join Public Group Tests

    [Fact]
    public void ValidateAndSign_WithUserJoiningPublicGroup_ShouldSucceed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var userAddress = TestDataFactory.CreateAddress();
        var publicGroup = TestDataFactory.CreatePublicGroupFeed(feedId);

        MockServices.ConfigureFeedsStorageForJoinGroup(mocker, publicGroup);

        var handler = mocker.CreateInstance<JoinGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateJoinGroupFeedPayload(feedId, userAddress);
        var transaction = TestDataFactory.CreateJoinGroupFeedSignedTransaction(payload, userAddress);

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
        var userAddress = TestDataFactory.CreateAddress();
        var differentAddress = TestDataFactory.CreateAddress();
        var publicGroup = TestDataFactory.CreatePublicGroupFeed(feedId);

        MockServices.ConfigureFeedsStorageForJoinGroup(mocker, publicGroup);

        var handler = mocker.CreateInstance<JoinGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateJoinGroupFeedPayload(feedId, userAddress);
        // Sign with different address than the joining user
        var transaction = TestDataFactory.CreateJoinGroupFeedSignedTransaction(payload, differentAddress);

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
        MockServices.ConfigureFeedsStorageForJoinGroup(mocker, null);

        var feedId = TestDataFactory.CreateFeedId();
        var userAddress = TestDataFactory.CreateAddress();

        var handler = mocker.CreateInstance<JoinGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateJoinGroupFeedPayload(feedId, userAddress);
        var transaction = TestDataFactory.CreateJoinGroupFeedSignedTransaction(payload, userAddress);

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
        var userAddress = TestDataFactory.CreateAddress();
        var deletedGroup = TestDataFactory.CreatePublicGroupFeed(feedId, isDeleted: true);

        MockServices.ConfigureFeedsStorageForJoinGroup(mocker, deletedGroup);

        var handler = mocker.CreateInstance<JoinGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateJoinGroupFeedPayload(feedId, userAddress);
        var transaction = TestDataFactory.CreateJoinGroupFeedSignedTransaction(payload, userAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithPrivateGroupWithoutInvitation_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var userAddress = TestDataFactory.CreateAddress();
        // Private group (isPublic = false)
        var privateGroup = TestDataFactory.CreateGroupFeed(feedId);

        MockServices.ConfigureFeedsStorageForJoinGroup(mocker, privateGroup);

        var handler = mocker.CreateInstance<JoinGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateJoinGroupFeedPayload(feedId, userAddress, invitationSignature: null);
        var transaction = TestDataFactory.CreateJoinGroupFeedSignedTransaction(payload, userAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Membership Validation Tests

    [Fact]
    public void ValidateAndSign_WithBannedUser_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var userAddress = TestDataFactory.CreateAddress();
        var publicGroup = TestDataFactory.CreatePublicGroupFeed(feedId);
        var bannedParticipant = TestDataFactory.CreateParticipantEntity(feedId, userAddress, ParticipantType.Banned);

        MockServices.ConfigureFeedsStorageForJoinGroup(mocker, publicGroup, bannedParticipant);

        var handler = mocker.CreateInstance<JoinGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateJoinGroupFeedPayload(feedId, userAddress);
        var transaction = TestDataFactory.CreateJoinGroupFeedSignedTransaction(payload, userAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithActiveMember_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var userAddress = TestDataFactory.CreateAddress();
        var publicGroup = TestDataFactory.CreatePublicGroupFeed(feedId);
        // Active member (LeftAtBlock = null)
        var activeMember = TestDataFactory.CreateParticipantEntity(feedId, userAddress, ParticipantType.Member);

        MockServices.ConfigureFeedsStorageForJoinGroup(mocker, publicGroup, activeMember);

        var handler = mocker.CreateInstance<JoinGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateJoinGroupFeedPayload(feedId, userAddress);
        var transaction = TestDataFactory.CreateJoinGroupFeedSignedTransaction(payload, userAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Cooldown Tests

    [Fact]
    public void ValidateAndSign_WithUserStillInCooldown_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var userAddress = TestDataFactory.CreateAddress();
        var publicGroup = TestDataFactory.CreatePublicGroupFeed(feedId);

        // User left at block 400, current block is 450 (only 50 blocks elapsed, need 100)
        var formerMember = TestDataFactory.CreateParticipantEntityWithHistory(
            feedId,
            userAddress,
            ParticipantType.Member,
            leftAtBlock: new BlockIndex(400),
            lastLeaveBlock: new BlockIndex(400));

        MockServices.ConfigureFeedsStorageForJoinGroup(mocker, publicGroup, formerMember, currentBlock: 450);

        var handler = mocker.CreateInstance<JoinGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateJoinGroupFeedPayload(feedId, userAddress);
        var transaction = TestDataFactory.CreateJoinGroupFeedSignedTransaction(payload, userAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithUserPastCooldown_ShouldSucceed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var userAddress = TestDataFactory.CreateAddress();
        var publicGroup = TestDataFactory.CreatePublicGroupFeed(feedId);

        // User left at block 300, current block is 500 (200 blocks elapsed, > 100 cooldown)
        var formerMember = TestDataFactory.CreateParticipantEntityWithHistory(
            feedId,
            userAddress,
            ParticipantType.Member,
            leftAtBlock: new BlockIndex(300),
            lastLeaveBlock: new BlockIndex(300));

        MockServices.ConfigureFeedsStorageForJoinGroup(mocker, publicGroup, formerMember, currentBlock: 500);

        var handler = mocker.CreateInstance<JoinGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateJoinGroupFeedPayload(feedId, userAddress);
        var transaction = TestDataFactory.CreateJoinGroupFeedSignedTransaction(payload, userAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion
}
