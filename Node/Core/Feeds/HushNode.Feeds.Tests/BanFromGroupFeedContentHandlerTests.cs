using FluentAssertions;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Feeds.Model;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for BanFromGroupFeedContentHandler - validation and signing of Ban transactions.
/// Ban operations are cryptographic (unlike Block) - they trigger key rotation.
/// </summary>
public class BanFromGroupFeedContentHandlerTests
{
    #region CanValidate Tests

    [Fact]
    public void CanValidate_WithBanFromGroupFeedPayloadKind_ShouldReturnTrue()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<BanFromGroupFeedContentHandler>();

        // Act
        var result = handler.CanValidate(BanFromGroupFeedPayloadHandler.BanFromGroupFeedPayloadKind);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanValidate_WithOtherPayloadKind_ShouldReturnFalse()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<BanFromGroupFeedContentHandler>();

        // Act
        var result = handler.CanValidate(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Admin Validation Tests

    [Fact]
    public void ValidateAndSign_WithAdminBanningMember_ShouldSucceed()
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

        var handler = mocker.CreateInstance<BanFromGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateBanFromGroupFeedPayload(feedId, adminAddress, memberAddress);
        var transaction = TestDataFactory.CreateBanFromGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_WithAdminBanningBlockedMember_ShouldSucceed()
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

        var handler = mocker.CreateInstance<BanFromGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateBanFromGroupFeedPayload(feedId, adminAddress, blockedAddress);
        var transaction = TestDataFactory.CreateBanFromGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_WithNonAdminBanningMember_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = TestDataFactory.CreateAddress();
        var targetAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var memberParticipant = TestDataFactory.CreateParticipantEntity(feedId, memberAddress, ParticipantType.Member);
        var targetParticipant = TestDataFactory.CreateParticipantEntity(feedId, targetAddress, ParticipantType.Member);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, memberParticipant, targetParticipant);

        var handler = mocker.CreateInstance<BanFromGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateBanFromGroupFeedPayload(feedId, memberAddress, targetAddress);
        var transaction = TestDataFactory.CreateBanFromGroupFeedSignedTransaction(payload, memberAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithAdminBanningAdmin_ShouldReturnNull()
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

        var handler = mocker.CreateInstance<BanFromGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateBanFromGroupFeedPayload(feedId, adminAddress, otherAdminAddress);
        var transaction = TestDataFactory.CreateBanFromGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithAdminBanningSelf_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, adminParticipant, adminParticipant);

        var handler = mocker.CreateInstance<BanFromGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateBanFromGroupFeedPayload(feedId, adminAddress, adminAddress);
        var transaction = TestDataFactory.CreateBanFromGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Member State Validation Tests

    [Fact]
    public void ValidateAndSign_WithAlreadyBannedMember_ShouldReturnNull()
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

        var handler = mocker.CreateInstance<BanFromGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateBanFromGroupFeedPayload(feedId, adminAddress, bannedAddress);
        var transaction = TestDataFactory.CreateBanFromGroupFeedSignedTransaction(payload, adminAddress);

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

        var handler = mocker.CreateInstance<BanFromGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateBanFromGroupFeedPayload(feedId, adminAddress, nonMemberAddress);
        var transaction = TestDataFactory.CreateBanFromGroupFeedSignedTransaction(payload, adminAddress);

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
        var memberAddress = TestDataFactory.CreateAddress();

        var handler = mocker.CreateInstance<BanFromGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateBanFromGroupFeedPayload(feedId, adminAddress, memberAddress);
        var transaction = TestDataFactory.CreateBanFromGroupFeedSignedTransaction(payload, adminAddress);

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
        var memberAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId, isDeleted: true);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);
        var memberParticipant = TestDataFactory.CreateParticipantEntity(feedId, memberAddress, ParticipantType.Member);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, adminParticipant, memberParticipant);

        var handler = mocker.CreateInstance<BanFromGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateBanFromGroupFeedPayload(feedId, adminAddress, memberAddress);
        var transaction = TestDataFactory.CreateBanFromGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
