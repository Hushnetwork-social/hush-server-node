using FluentAssertions;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Feeds.Model;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for UnblockMemberContentHandler - validation and signing of UnblockMember transactions.
/// </summary>
public class UnblockMemberContentHandlerTests
{
    #region CanValidate Tests

    [Fact]
    public void CanValidate_WithUnblockMemberPayloadKind_ShouldReturnTrue()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<UnblockMemberContentHandler>();

        // Act
        var result = handler.CanValidate(UnblockMemberPayloadHandler.UnblockMemberPayloadKind);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanValidate_WithOtherPayloadKind_ShouldReturnFalse()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<UnblockMemberContentHandler>();

        // Act
        var result = handler.CanValidate(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Admin Validation Tests

    [Fact]
    public void ValidateAndSign_WithAdminUnblockingBlockedMember_ShouldSucceed()
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

        var handler = mocker.CreateInstance<UnblockMemberContentHandler>();
        var payload = TestDataFactory.CreateUnblockMemberPayload(feedId, adminAddress, blockedAddress);
        var transaction = TestDataFactory.CreateUnblockMemberSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_WithNonAdminUnblocking_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = TestDataFactory.CreateAddress();
        var blockedAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var memberParticipant = TestDataFactory.CreateParticipantEntity(feedId, memberAddress, ParticipantType.Member);
        var blockedParticipant = TestDataFactory.CreateParticipantEntity(feedId, blockedAddress, ParticipantType.Blocked);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, memberParticipant, blockedParticipant);

        var handler = mocker.CreateInstance<UnblockMemberContentHandler>();
        var payload = TestDataFactory.CreateUnblockMemberPayload(feedId, memberAddress, blockedAddress);
        var transaction = TestDataFactory.CreateUnblockMemberSignedTransaction(payload, memberAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithUnblockingNonBlockedMember_ShouldReturnNull()
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

        var handler = mocker.CreateInstance<UnblockMemberContentHandler>();
        var payload = TestDataFactory.CreateUnblockMemberPayload(feedId, adminAddress, memberAddress);
        var transaction = TestDataFactory.CreateUnblockMemberSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
