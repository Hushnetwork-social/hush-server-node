using FluentAssertions;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Feeds.Model;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for BlockMemberContentHandler - validation and signing of BlockMember transactions.
/// </summary>
public class BlockMemberContentHandlerTests
{
    #region CanValidate Tests

    [Fact]
    public void CanValidate_WithBlockMemberPayloadKind_ShouldReturnTrue()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<BlockMemberContentHandler>();

        // Act
        var result = handler.CanValidate(BlockMemberPayloadHandler.BlockMemberPayloadKind);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanValidate_WithOtherPayloadKind_ShouldReturnFalse()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<BlockMemberContentHandler>();

        // Act
        var result = handler.CanValidate(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Admin Validation Tests

    [Fact]
    public void ValidateAndSign_WithAdminBlockingMember_ShouldSucceed()
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

        var handler = mocker.CreateInstance<BlockMemberContentHandler>();
        var payload = TestDataFactory.CreateBlockMemberPayload(feedId, adminAddress, memberAddress);
        var transaction = TestDataFactory.CreateBlockMemberSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_WithNonAdminBlockingMember_ShouldReturnNull()
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

        var handler = mocker.CreateInstance<BlockMemberContentHandler>();
        var payload = TestDataFactory.CreateBlockMemberPayload(feedId, memberAddress, targetAddress);
        var transaction = TestDataFactory.CreateBlockMemberSignedTransaction(payload, memberAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithAdminBlockingAdmin_ShouldReturnNull()
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

        var handler = mocker.CreateInstance<BlockMemberContentHandler>();
        var payload = TestDataFactory.CreateBlockMemberPayload(feedId, adminAddress, otherAdminAddress);
        var transaction = TestDataFactory.CreateBlockMemberSignedTransaction(payload, adminAddress);

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

        var handler = mocker.CreateInstance<BlockMemberContentHandler>();
        var payload = TestDataFactory.CreateBlockMemberPayload(feedId, adminAddress, memberAddress);
        var transaction = TestDataFactory.CreateBlockMemberSignedTransaction(payload, adminAddress);

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

        var handler = mocker.CreateInstance<BlockMemberContentHandler>();
        var payload = TestDataFactory.CreateBlockMemberPayload(feedId, adminAddress, memberAddress);
        var transaction = TestDataFactory.CreateBlockMemberSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
