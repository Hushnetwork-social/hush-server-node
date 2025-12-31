using FluentAssertions;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Feeds.Model;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for AddMemberToGroupFeedContentHandler - validation and signing of AddMemberToGroupFeed transactions.
/// </summary>
public class AddMemberToGroupFeedContentHandlerTests
{
    #region CanValidate Tests

    [Fact]
    public void CanValidate_WithAddMemberToGroupFeedPayloadKind_ShouldReturnTrue()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<AddMemberToGroupFeedContentHandler>();

        // Act
        var result = handler.CanValidate(AddMemberToGroupFeedPayloadHandler.AddMemberToGroupFeedPayloadKind);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanValidate_WithOtherPayloadKind_ShouldReturnFalse()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<AddMemberToGroupFeedContentHandler>();

        // Act
        var result = handler.CanValidate(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Admin Adding Member Tests

    [Fact]
    public void ValidateAndSign_WithAdminAddingNewMember_ShouldSucceed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var newMemberAddress = TestDataFactory.CreateAddress();
        var encryptedKey = TestDataFactory.CreateEncryptedKey();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);

        MockServices.ConfigureFeedsStorageForAddMember(mocker, groupFeed, adminParticipant);

        var handler = mocker.CreateInstance<AddMemberToGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateAddMemberToGroupFeedPayload(feedId, adminAddress, newMemberAddress, encryptedKey);
        var transaction = TestDataFactory.CreateAddMemberToGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_WithNonAdminAddingMember_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var memberAddress = TestDataFactory.CreateAddress();
        var newMemberAddress = TestDataFactory.CreateAddress();
        var encryptedKey = TestDataFactory.CreateEncryptedKey();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        // Regular member, not admin
        var memberParticipant = TestDataFactory.CreateParticipantEntity(feedId, memberAddress, ParticipantType.Member);

        MockServices.ConfigureFeedsStorageForAddMember(mocker, groupFeed, memberParticipant);

        var handler = mocker.CreateInstance<AddMemberToGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateAddMemberToGroupFeedPayload(feedId, memberAddress, newMemberAddress, encryptedKey);
        var transaction = TestDataFactory.CreateAddMemberToGroupFeedSignedTransaction(payload, memberAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithMismatchedSignatory_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var differentAddress = TestDataFactory.CreateAddress();
        var newMemberAddress = TestDataFactory.CreateAddress();
        var encryptedKey = TestDataFactory.CreateEncryptedKey();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);

        MockServices.ConfigureFeedsStorageForAddMember(mocker, groupFeed, adminParticipant);

        var handler = mocker.CreateInstance<AddMemberToGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateAddMemberToGroupFeedPayload(feedId, adminAddress, newMemberAddress, encryptedKey);
        // Sign with different address
        var transaction = TestDataFactory.CreateAddMemberToGroupFeedSignedTransaction(payload, differentAddress);

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
        MockServices.ConfigureFeedsStorageForAddMember(mocker, null);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var newMemberAddress = TestDataFactory.CreateAddress();
        var encryptedKey = TestDataFactory.CreateEncryptedKey();

        var handler = mocker.CreateInstance<AddMemberToGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateAddMemberToGroupFeedPayload(feedId, adminAddress, newMemberAddress, encryptedKey);
        var transaction = TestDataFactory.CreateAddMemberToGroupFeedSignedTransaction(payload, adminAddress);

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
        var newMemberAddress = TestDataFactory.CreateAddress();
        var encryptedKey = TestDataFactory.CreateEncryptedKey();
        var deletedGroup = TestDataFactory.CreateGroupFeed(feedId, isDeleted: true);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);

        MockServices.ConfigureFeedsStorageForAddMember(mocker, deletedGroup, adminParticipant);

        var handler = mocker.CreateInstance<AddMemberToGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateAddMemberToGroupFeedPayload(feedId, adminAddress, newMemberAddress, encryptedKey);
        var transaction = TestDataFactory.CreateAddMemberToGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Target Validation Tests

    [Fact]
    public void ValidateAndSign_WithExistingActiveMember_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var feedId = TestDataFactory.CreateFeedId();
        var adminAddress = TestDataFactory.CreateAddress();
        var existingMemberAddress = TestDataFactory.CreateAddress();
        var encryptedKey = TestDataFactory.CreateEncryptedKey();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);
        // Member already exists and is active
        var existingMember = TestDataFactory.CreateParticipantEntity(feedId, existingMemberAddress, ParticipantType.Member);

        MockServices.ConfigureFeedsStorageForAddMember(mocker, groupFeed, adminParticipant, existingMember);

        var handler = mocker.CreateInstance<AddMemberToGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateAddMemberToGroupFeedPayload(feedId, adminAddress, existingMemberAddress, encryptedKey);
        var transaction = TestDataFactory.CreateAddMemberToGroupFeedSignedTransaction(payload, adminAddress);

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
        var adminAddress = TestDataFactory.CreateAddress();
        var bannedUserAddress = TestDataFactory.CreateAddress();
        var encryptedKey = TestDataFactory.CreateEncryptedKey();
        var groupFeed = TestDataFactory.CreateGroupFeed(feedId);
        var adminParticipant = TestDataFactory.CreateParticipantEntity(feedId, adminAddress, ParticipantType.Admin);
        // User is banned
        var bannedUser = TestDataFactory.CreateParticipantEntity(feedId, bannedUserAddress, ParticipantType.Banned);

        MockServices.ConfigureFeedsStorageForAddMember(mocker, groupFeed, adminParticipant, bannedUser);

        var handler = mocker.CreateInstance<AddMemberToGroupFeedContentHandler>();
        var payload = TestDataFactory.CreateAddMemberToGroupFeedPayload(feedId, adminAddress, bannedUserAddress, encryptedKey);
        var transaction = TestDataFactory.CreateAddMemberToGroupFeedSignedTransaction(payload, adminAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
