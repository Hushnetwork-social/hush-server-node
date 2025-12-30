using FluentAssertions;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Feeds.Model;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for UpdateGroupFeedDescriptionContentHandler - validation and signing of UpdateGroupFeedDescription transactions.
/// </summary>
public class UpdateGroupFeedDescriptionContentHandlerTests
{
    #region CanValidate Tests

    [Fact]
    public void CanValidate_WithUpdateGroupFeedDescriptionPayloadKind_ShouldReturnTrue()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<UpdateGroupFeedDescriptionContentHandler>();

        // Act
        var result = handler.CanValidate(UpdateGroupFeedDescriptionPayloadHandler.UpdateGroupFeedDescriptionPayloadKind);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanValidate_WithOtherPayloadKind_ShouldReturnFalse()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<UpdateGroupFeedDescriptionContentHandler>();

        // Act
        var result = handler.CanValidate(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Description Validation Tests

    [Fact]
    public void ValidateAndSign_WithValidDescription_ShouldSucceed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, adminParticipant);

        var handler = mocker.CreateInstance<UpdateGroupFeedDescriptionContentHandler>();
        var payload = TestDataFactory.CreateUpdateDescriptionPayload(feedId, adminAddress, "New Description");
        var transaction = TestDataFactory.CreateUpdateDescriptionSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_WithEmptyDescription_ShouldSucceed()
    {
        // Arrange - Empty description is allowed per requirements
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, adminParticipant);

        var handler = mocker.CreateInstance<UpdateGroupFeedDescriptionContentHandler>();
        var payload = TestDataFactory.CreateUpdateDescriptionPayload(feedId, adminAddress, "");
        var transaction = TestDataFactory.CreateUpdateDescriptionSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region Admin Validation Tests

    [Fact]
    public void ValidateAndSign_WithNonAdminUpdatingDescription_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var memberParticipant = TestDataFactory.CreateParticipantEntity(feedId, memberAddress, ParticipantType.Member);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, memberParticipant);

        var handler = mocker.CreateInstance<UpdateGroupFeedDescriptionContentHandler>();
        var payload = TestDataFactory.CreateUpdateDescriptionPayload(feedId, memberAddress, "New Description");
        var transaction = TestDataFactory.CreateUpdateDescriptionSignedTransaction(payload, memberAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Group Validation Tests

    [Fact]
    public void ValidateAndSign_WithDeletedGroup_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId, isDeleted: true);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, adminParticipant);

        var handler = mocker.CreateInstance<UpdateGroupFeedDescriptionContentHandler>();
        var payload = TestDataFactory.CreateUpdateDescriptionPayload(feedId, adminAddress, "New Description");
        var transaction = TestDataFactory.CreateUpdateDescriptionSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
