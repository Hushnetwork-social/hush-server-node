using FluentAssertions;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Feeds.Model;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for DeleteGroupFeedContentHandler - validation and signing of DeleteGroupFeed transactions.
/// </summary>
public class DeleteGroupFeedContentHandlerTests
{
    #region CanValidate Tests

    [Fact]
    public void CanValidate_WithDeleteGroupFeedPayloadKind_ShouldReturnTrue()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<DeleteGroupFeedContentHandler>();

        // Act
        var result = handler.CanValidate(DeleteGroupFeedPayloadHandler.DeleteGroupFeedPayloadKind);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanValidate_WithOtherPayloadKind_ShouldReturnFalse()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<DeleteGroupFeedContentHandler>();

        // Act
        var result = handler.CanValidate(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Delete Validation Tests

    [Fact]
    public void ValidateAndSign_WithAdminDeleting_ShouldSucceed()
    {
        // Arrange - Any admin can delete the group
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, adminParticipant, adminCount: 1);

        var handler = mocker.CreateInstance<DeleteGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateDeleteGroupFeedPayload(feedId, adminAddress);
        var transaction = TestDataFactory.CreateDeleteGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_WithMultipleAdmins_ShouldSucceed()
    {
        // Arrange - Any admin can delete, even when multiple admins exist
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, adminParticipant, adminCount: 2);

        var handler = mocker.CreateInstance<DeleteGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateDeleteGroupFeedPayload(feedId, adminAddress);
        var transaction = TestDataFactory.CreateDeleteGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_WithNonAdminDeleting_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var memberParticipant = TestDataFactory.CreateParticipantEntity(feedId, memberAddress, ParticipantType.Member);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, memberParticipant, adminCount: 1);

        var handler = mocker.CreateInstance<DeleteGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateDeleteGroupFeedPayload(feedId, memberAddress);
        var transaction = TestDataFactory.CreateDeleteGroupFeedSignedTransaction(payload, memberAddress);

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
        MockServices.ConfigureFeedsStorageForAdminControls(mocker, null, adminCount: 1);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();

        var handler = mocker.CreateInstance<DeleteGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateDeleteGroupFeedPayload(feedId, adminAddress);
        var transaction = TestDataFactory.CreateDeleteGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithAlreadyDeletedGroup_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId, isDeleted: true);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);

        MockServices.ConfigureFeedsStorageForAdminControls(mocker, groupFeed, adminParticipant, adminCount: 1);

        var handler = mocker.CreateInstance<DeleteGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateDeleteGroupFeedPayload(feedId, adminAddress);
        var transaction = TestDataFactory.CreateDeleteGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
