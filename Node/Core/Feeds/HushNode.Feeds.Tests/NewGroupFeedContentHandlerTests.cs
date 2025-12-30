using FluentAssertions;
using HushNode.Credentials;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Feeds.Model;
using Moq;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for NewGroupFeedContentHandler - validation and signing of NewGroupFeed transactions.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class NewGroupFeedContentHandlerTests
{
    #region CanValidate Tests

    [Fact]
    public void CanValidate_WithNewGroupFeedPayloadKind_ShouldReturnTrue()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<NewGroupFeedContentHandler>();

        // Act
        var result = handler.CanValidate(NewGroupFeedPayloadHandler.NewGroupFeedPayloadKind);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanValidate_WithOtherPayloadKind_ShouldReturnFalse()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<NewGroupFeedContentHandler>();
        var otherKind = Guid.NewGuid();

        // Act
        var result = handler.CanValidate(otherKind);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Title Validation Tests

    [Fact]
    public void ValidateAndSign_WithEmptyTitle_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<NewGroupFeedContentHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateValidPayload(creatorAddress, title: "");
        var transaction = TestDataFactory.CreateSignedTransaction(payload, creatorAddress);

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
        var handler = mocker.CreateInstance<NewGroupFeedContentHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateValidPayload(creatorAddress, title: "   ");
        var transaction = TestDataFactory.CreateSignedTransaction(payload, creatorAddress);

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
        var handler = mocker.CreateInstance<NewGroupFeedContentHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var longTitle = new string('A', 101);
        var payload = TestDataFactory.CreateValidPayload(creatorAddress, title: longTitle);
        var transaction = TestDataFactory.CreateSignedTransaction(payload, creatorAddress);

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
        var handler = mocker.CreateInstance<NewGroupFeedContentHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var maxTitle = new string('A', 100);
        var payload = TestDataFactory.CreateValidPayload(creatorAddress, title: maxTitle);
        var transaction = TestDataFactory.CreateSignedTransaction(payload, creatorAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region Participant Validation Tests

    [Fact]
    public void ValidateAndSign_WithZeroParticipants_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<NewGroupFeedContentHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = new NewGroupFeedPayload(
            TestDataFactory.CreateFeedId(),
            "Test Group",
            "Description",
            false,
            Array.Empty<GroupFeedParticipant>());
        var transaction = TestDataFactory.CreateSignedTransaction(payload, creatorAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithSingleParticipant_AdminOnly_ShouldSucceed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<NewGroupFeedContentHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateValidPayload(creatorAddress, participantCount: 1);
        var transaction = TestDataFactory.CreateSignedTransaction(payload, creatorAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_WithMultipleParticipants_ShouldSucceed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<NewGroupFeedContentHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateValidPayload(creatorAddress, participantCount: 5);
        var transaction = TestDataFactory.CreateSignedTransaction(payload, creatorAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region Creator in Participant List Tests

    [Fact]
    public void ValidateAndSign_WithCreatorNotInParticipantList_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<NewGroupFeedContentHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var differentAddress = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        var participants = new[]
        {
            new GroupFeedParticipant(feedId, differentAddress, ParticipantType.Member, TestDataFactory.CreateEncryptedKey(), 0)
        };
        var payload = new NewGroupFeedPayload(feedId, "Test Group", "Description", false, participants);
        var transaction = TestDataFactory.CreateSignedTransaction(payload, creatorAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithCreatorInParticipantList_ShouldSucceed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<NewGroupFeedContentHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateValidPayload(creatorAddress, participantCount: 3);
        var transaction = TestDataFactory.CreateSignedTransaction(payload, creatorAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region Duplicate Participant Tests

    [Fact]
    public void ValidateAndSign_WithDuplicateParticipants_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<NewGroupFeedContentHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var duplicateAddress = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        var participants = new[]
        {
            new GroupFeedParticipant(feedId, creatorAddress, ParticipantType.Member, TestDataFactory.CreateEncryptedKey(), 0),
            new GroupFeedParticipant(feedId, duplicateAddress, ParticipantType.Member, TestDataFactory.CreateEncryptedKey(), 0),
            new GroupFeedParticipant(feedId, duplicateAddress, ParticipantType.Member, TestDataFactory.CreateEncryptedKey(), 0)
        };
        var payload = new NewGroupFeedPayload(feedId, "Test Group", "Description", false, participants);
        var transaction = TestDataFactory.CreateSignedTransaction(payload, creatorAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithUniqueParticipants_ShouldSucceed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<NewGroupFeedContentHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateValidPayload(creatorAddress, participantCount: 3);
        var transaction = TestDataFactory.CreateSignedTransaction(payload, creatorAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region Empty Address Tests

    [Fact]
    public void ValidateAndSign_WithEmptyParticipantAddress_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<NewGroupFeedContentHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        var participants = new[]
        {
            new GroupFeedParticipant(feedId, creatorAddress, ParticipantType.Member, TestDataFactory.CreateEncryptedKey(), 0),
            new GroupFeedParticipant(feedId, "", ParticipantType.Member, TestDataFactory.CreateEncryptedKey(), 0)
        };
        var payload = new NewGroupFeedPayload(feedId, "Test Group", "Description", false, participants);
        var transaction = TestDataFactory.CreateSignedTransaction(payload, creatorAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithWhitespaceParticipantAddress_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<NewGroupFeedContentHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        var participants = new[]
        {
            new GroupFeedParticipant(feedId, creatorAddress, ParticipantType.Member, TestDataFactory.CreateEncryptedKey(), 0),
            new GroupFeedParticipant(feedId, "   ", ParticipantType.Member, TestDataFactory.CreateEncryptedKey(), 0)
        };
        var payload = new NewGroupFeedPayload(feedId, "Test Group", "Description", false, participants);
        var transaction = TestDataFactory.CreateSignedTransaction(payload, creatorAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Valid Transaction Tests

    [Fact]
    public void ValidateAndSign_WithValidTransaction_ShouldReturnSignedTransaction()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<NewGroupFeedContentHandler>();
        var creatorAddress = TestDataFactory.CreateAddress();
        var payload = TestDataFactory.CreateValidPayload(creatorAddress, participantCount: 2);
        var transaction = TestDataFactory.CreateSignedTransaction(payload, creatorAddress);

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion
}
