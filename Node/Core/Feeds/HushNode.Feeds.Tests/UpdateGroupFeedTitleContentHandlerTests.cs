using FluentAssertions;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Feeds.Model;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for UpdateGroupFeedTitleContentHandler - validation and signing of UpdateGroupFeedTitle transactions.
/// </summary>
public class UpdateGroupFeedTitleContentHandlerTests
{
    #region CanValidate Tests

    [Fact]
    public void CanValidate_WithUpdateGroupFeedTitlePayloadKind_ShouldReturnTrue()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<UpdateGroupFeedTitleContentHandler>();

        // Act
        var result = handler.CanValidate(UpdateGroupFeedTitlePayloadHandler.UpdateGroupFeedTitlePayloadKind);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanValidate_WithOtherPayloadKind_ShouldReturnFalse()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<UpdateGroupFeedTitleContentHandler>();

        // Act
        var result = handler.CanValidate(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Title Validation Tests

    [Fact]
    public void ValidateAndSign_WithValidTitle_ShouldSucceed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, adminParticipant);

        var handler = mocker.CreateInstance<UpdateGroupFeedTitleContentHandler>();
        var payload = TestDataFactory.CreateUpdateTitlePayload(feedId, adminAddress, "New Valid Title");
        var transaction = TestDataFactory.CreateUpdateTitleSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_WithEmptyTitle_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, adminParticipant);

        var handler = mocker.CreateInstance<UpdateGroupFeedTitleContentHandler>();
        var payload = TestDataFactory.CreateUpdateTitlePayload(feedId, adminAddress, "");
        var transaction = TestDataFactory.CreateUpdateTitleSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithWhitespaceTitle_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, adminParticipant);

        var handler = mocker.CreateInstance<UpdateGroupFeedTitleContentHandler>();
        var payload = TestDataFactory.CreateUpdateTitlePayload(feedId, adminAddress, "   ");
        var transaction = TestDataFactory.CreateUpdateTitleSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithTitleOver100Characters_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, adminParticipant);

        var handler = mocker.CreateInstance<UpdateGroupFeedTitleContentHandler>();
        var longTitle = new string('A', 101);
        var payload = TestDataFactory.CreateUpdateTitlePayload(feedId, adminAddress, longTitle);
        var transaction = TestDataFactory.CreateUpdateTitleSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithTitleExactly100Characters_ShouldSucceed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, adminParticipant);

        var handler = mocker.CreateInstance<UpdateGroupFeedTitleContentHandler>();
        var maxTitle = new string('A', 100);
        var payload = TestDataFactory.CreateUpdateTitlePayload(feedId, adminAddress, maxTitle);
        var transaction = TestDataFactory.CreateUpdateTitleSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region Admin Validation Tests

    [Fact]
    public void ValidateAndSign_WithNonAdminUpdatingTitle_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var memberParticipant = TestDataFactory.CreateParticipantEntity(feedId, memberAddress, ParticipantType.Member);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, memberParticipant);

        var handler = mocker.CreateInstance<UpdateGroupFeedTitleContentHandler>();
        var payload = TestDataFactory.CreateUpdateTitlePayload(feedId, memberAddress, "New Title");
        var transaction = TestDataFactory.CreateUpdateTitleSignedTransaction(payload, memberAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
