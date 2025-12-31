using FluentAssertions;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Feeds.Model;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for UnbanFromGroupFeedContentHandler - validation and signing of Unban transactions.
/// Unban operations are cryptographic (unlike Unblock) - they trigger key rotation to include
/// the unbanned member in future key distributions.
/// NOTE: Unbanned member CANNOT read messages from the ban period (security by design).
/// </summary>
public class UnbanFromGroupFeedContentHandlerTests
{
    #region CanValidate Tests

    [Fact]
    public void CanValidate_WithUnbanFromGroupFeedPayloadKind_ShouldReturnTrue()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<UnbanFromGroupFeedContentHandler>();

        // Act
        var result = handler.CanValidate(UnbanFromGroupFeedPayloadHandler.UnbanFromGroupFeedPayloadKind);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanValidate_WithOtherPayloadKind_ShouldReturnFalse()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<UnbanFromGroupFeedContentHandler>();

        // Act
        var result = handler.CanValidate(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Admin Validation Tests

    [Fact]
    public void ValidateAndSign_WithAdminUnbanningBannedMember_ShouldSucceed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var bannedAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);
        var bannedParticipant = TestDataFactory.CreateParticipantEntity(feedId, bannedAddress, ParticipantType.Banned);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, adminParticipant, bannedParticipant);

        var handler = mocker.CreateInstance<UnbanFromGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateUnbanFromGroupFeedPayload(feedId, adminAddress, bannedAddress);
        var transaction = TestDataFactory.CreateUnbanFromGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_WithNonAdminUnbanningMember_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = TestDataFactory.CreateAddress();
        var bannedAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var memberParticipant = TestDataFactory.CreateParticipantEntity(feedId, memberAddress, ParticipantType.Member);
        var bannedParticipant = TestDataFactory.CreateParticipantEntity(feedId, bannedAddress, ParticipantType.Banned);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, memberParticipant, bannedParticipant);

        var handler = mocker.CreateInstance<UnbanFromGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateUnbanFromGroupFeedPayload(feedId, memberAddress, bannedAddress);
        var transaction = TestDataFactory.CreateUnbanFromGroupFeedSignedTransaction(payload, memberAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Member State Validation Tests

    [Fact]
    public void ValidateAndSign_WithUnbanningActiveMember_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var memberAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);
        var memberParticipant = TestDataFactory.CreateParticipantEntity(feedId, memberAddress, ParticipantType.Member);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, adminParticipant, memberParticipant);

        var handler = mocker.CreateInstance<UnbanFromGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateUnbanFromGroupFeedPayload(feedId, adminAddress, memberAddress);
        var transaction = TestDataFactory.CreateUnbanFromGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithUnbanningBlockedMember_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var blockedAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);
        var blockedParticipant = TestDataFactory.CreateParticipantEntity(feedId, blockedAddress, ParticipantType.Blocked);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, adminParticipant, blockedParticipant);

        var handler = mocker.CreateInstance<UnbanFromGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateUnbanFromGroupFeedPayload(feedId, adminAddress, blockedAddress);
        var transaction = TestDataFactory.CreateUnbanFromGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithUnbanningAdmin_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var otherAdminAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);
        var otherAdminParticipant = TestDataFactory.CreateParticipantEntity(feedId, otherAdminAddress, ParticipantType.Admin);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, adminParticipant, otherAdminParticipant);

        var handler = mocker.CreateInstance<UnbanFromGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateUnbanFromGroupFeedPayload(feedId, adminAddress, otherAdminAddress);
        var transaction = TestDataFactory.CreateUnbanFromGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithNonParticipant_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var nonMemberAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);

        // Target is not a participant (null)
        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, adminParticipant, null);

        var handler = mocker.CreateInstance<UnbanFromGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateUnbanFromGroupFeedPayload(feedId, adminAddress, nonMemberAddress);
        var transaction = TestDataFactory.CreateUnbanFromGroupFeedSignedTransaction(payload, adminAddress);

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
        MockServices.ConfigureFeedsStorageForAdminControls(mocker, null);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var bannedAddress = TestDataFactory.CreateAddress();

        var handler = mocker.CreateInstance<UnbanFromGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateUnbanFromGroupFeedPayload(feedId, adminAddress, bannedAddress);
        var transaction = TestDataFactory.CreateUnbanFromGroupFeedSignedTransaction(payload, adminAddress);

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
        var adminAddress = TestDataFactory.CreateAddress();
        var bannedAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId, isDeleted: true);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);
        var bannedParticipant = TestDataFactory.CreateParticipantEntity(feedId, bannedAddress, ParticipantType.Banned);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, adminParticipant, bannedParticipant);

        var handler = mocker.CreateInstance<UnbanFromGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateUnbanFromGroupFeedPayload(feedId, adminAddress, bannedAddress);
        var transaction = TestDataFactory.CreateUnbanFromGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
