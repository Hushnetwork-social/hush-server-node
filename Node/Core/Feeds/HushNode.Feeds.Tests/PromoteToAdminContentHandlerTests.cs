using FluentAssertions;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Feeds.Model;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for PromoteToAdminContentHandler - validation and signing of PromoteToAdmin transactions.
/// </summary>
public class PromoteToAdminContentHandlerTests
{
    #region CanValidate Tests

    [Fact]
    public void CanValidate_WithPromoteToAdminPayloadKind_ShouldReturnTrue()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<PromoteToAdminContentHandler>();

        // Act
        var result = handler.CanValidate(PromoteToAdminPayloadHandler.PromoteToAdminPayloadKind);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanValidate_WithOtherPayloadKind_ShouldReturnFalse()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<PromoteToAdminContentHandler>();

        // Act
        var result = handler.CanValidate(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Promote Validation Tests

    [Fact]
    public void ValidateAndSign_WithAdminPromotingMember_ShouldSucceed()
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

        var handler = mocker.CreateInstance<PromoteToAdminContentHandler>();
        var payload = TestDataFactory.CreatePromoteToAdminPayload(feedId, adminAddress, memberAddress);
        var transaction = TestDataFactory.CreatePromoteToAdminSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_WithNonAdminPromoting_ShouldReturnNull()
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

        var handler = mocker.CreateInstance<PromoteToAdminContentHandler>();
        var payload = TestDataFactory.CreatePromoteToAdminPayload(feedId, memberAddress, targetAddress);
        var transaction = TestDataFactory.CreatePromoteToAdminSignedTransaction(payload, memberAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithPromotingBlockedMember_ShouldReturnNull()
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

        var handler = mocker.CreateInstance<PromoteToAdminContentHandler>();
        var payload = TestDataFactory.CreatePromoteToAdminPayload(feedId, adminAddress, blockedAddress);
        var transaction = TestDataFactory.CreatePromoteToAdminSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithPromotingExistingAdmin_ShouldReturnNull()
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

        var handler = mocker.CreateInstance<PromoteToAdminContentHandler>();
        var payload = TestDataFactory.CreatePromoteToAdminPayload(feedId, adminAddress, otherAdminAddress);
        var transaction = TestDataFactory.CreatePromoteToAdminSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
