using FluentAssertions;
using HushNode.Credentials;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Feeds.Model;
using Moq;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

/// <summary>
/// Tests for GroupFeedKeyRotationContentHandler - validation and signing of KeyRotation transactions.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class GroupFeedKeyRotationContentHandlerTests
{
    #region CanValidate Tests

    [Fact]
    public void CanValidate_WithKeyRotationPayloadKind_ShouldReturnTrue()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<GroupFeedKeyRotationContentHandler>();

        // Act
        var result = handler.CanValidate(GroupFeedKeyRotationPayloadHandler.GroupFeedKeyRotationPayloadKind);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanValidate_WithOtherPayloadKind_ShouldReturnFalse()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<GroupFeedKeyRotationContentHandler>();

        // Act
        var result = handler.CanValidate(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region KeyGeneration Sequence Validation Tests

    [Fact]
    public void ValidateAndSign_WithValidSequence_ShouldReturnSignedTransaction()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<GroupFeedKeyRotationContentHandler>();

        var feedId = TestDataFactory.CreateFeedId();
        var payload = TestDataFactory.CreateKeyRotationPayload(feedId, newKeyGeneration: 5, previousKeyGeneration: 4);
        var transaction = TestDataFactory.CreateKeyRotationSignedTransaction(payload, TestDataFactory.CreateAddress());

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_WithNonConsecutiveSequence_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<GroupFeedKeyRotationContentHandler>();

        var feedId = TestDataFactory.CreateFeedId();
        // NewKeyGeneration = 5, but Previous = 3 (should be 4)
        var payload = TestDataFactory.CreateKeyRotationPayload(feedId, newKeyGeneration: 5, previousKeyGeneration: 3);
        var transaction = TestDataFactory.CreateKeyRotationSignedTransaction(payload, TestDataFactory.CreateAddress());

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithZeroKeyGeneration_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<GroupFeedKeyRotationContentHandler>();

        var feedId = TestDataFactory.CreateFeedId();
        // NewKeyGeneration 0 is created with the group, not via rotation
        var payload = TestDataFactory.CreateKeyRotationPayload(feedId, newKeyGeneration: 0, previousKeyGeneration: -1);
        var transaction = TestDataFactory.CreateKeyRotationSignedTransaction(payload, TestDataFactory.CreateAddress());

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithFirstRotation_ShouldSucceed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<GroupFeedKeyRotationContentHandler>();

        var feedId = TestDataFactory.CreateFeedId();
        // First rotation: KeyGeneration 0 -> 1
        var payload = TestDataFactory.CreateKeyRotationPayload(feedId, newKeyGeneration: 1, previousKeyGeneration: 0);
        var transaction = TestDataFactory.CreateKeyRotationSignedTransaction(payload, TestDataFactory.CreateAddress());

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region EncryptedKeys Validation Tests

    [Fact]
    public void ValidateAndSign_WithEmptyEncryptedKeys_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<GroupFeedKeyRotationContentHandler>();

        var feedId = TestDataFactory.CreateFeedId();
        var payload = new GroupFeedKeyRotationPayload(
            feedId,
            NewKeyGeneration: 2,
            PreviousKeyGeneration: 1,
            ValidFromBlock: 100,
            EncryptedKeys: Array.Empty<GroupFeedEncryptedKey>(),
            RotationTrigger: RotationTrigger.Leave);
        var transaction = TestDataFactory.CreateKeyRotationSignedTransaction(payload, TestDataFactory.CreateAddress());

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithDuplicateMemberAddresses_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<GroupFeedKeyRotationContentHandler>();

        var feedId = TestDataFactory.CreateFeedId();
        var duplicateAddress = TestDataFactory.CreateAddress();
        var payload = new GroupFeedKeyRotationPayload(
            feedId,
            NewKeyGeneration: 2,
            PreviousKeyGeneration: 1,
            ValidFromBlock: 100,
            EncryptedKeys: new[]
            {
                new GroupFeedEncryptedKey(duplicateAddress, Convert.ToBase64String(new byte[128])),
                new GroupFeedEncryptedKey(duplicateAddress, Convert.ToBase64String(new byte[128]))
            },
            RotationTrigger: RotationTrigger.Ban);
        var transaction = TestDataFactory.CreateKeyRotationSignedTransaction(payload, TestDataFactory.CreateAddress());

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithEmptyMemberAddress_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<GroupFeedKeyRotationContentHandler>();

        var feedId = TestDataFactory.CreateFeedId();
        var payload = new GroupFeedKeyRotationPayload(
            feedId,
            NewKeyGeneration: 2,
            PreviousKeyGeneration: 1,
            ValidFromBlock: 100,
            EncryptedKeys: new[]
            {
                new GroupFeedEncryptedKey(TestDataFactory.CreateAddress(), Convert.ToBase64String(new byte[128])),
                new GroupFeedEncryptedKey("", Convert.ToBase64String(new byte[128]))
            },
            RotationTrigger: RotationTrigger.Unban);
        var transaction = TestDataFactory.CreateKeyRotationSignedTransaction(payload, TestDataFactory.CreateAddress());

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithWhitespaceMemberAddress_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<GroupFeedKeyRotationContentHandler>();

        var feedId = TestDataFactory.CreateFeedId();
        var payload = new GroupFeedKeyRotationPayload(
            feedId,
            NewKeyGeneration: 2,
            PreviousKeyGeneration: 1,
            ValidFromBlock: 100,
            EncryptedKeys: new[]
            {
                new GroupFeedEncryptedKey(TestDataFactory.CreateAddress(), Convert.ToBase64String(new byte[128])),
                new GroupFeedEncryptedKey("   ", Convert.ToBase64String(new byte[128]))
            },
            RotationTrigger: RotationTrigger.Manual);
        var transaction = TestDataFactory.CreateKeyRotationSignedTransaction(payload, TestDataFactory.CreateAddress());

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithEmptyEncryptedKey_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<GroupFeedKeyRotationContentHandler>();

        var feedId = TestDataFactory.CreateFeedId();
        var payload = new GroupFeedKeyRotationPayload(
            feedId,
            NewKeyGeneration: 2,
            PreviousKeyGeneration: 1,
            ValidFromBlock: 100,
            EncryptedKeys: new[]
            {
                new GroupFeedEncryptedKey(TestDataFactory.CreateAddress(), ""),
                new GroupFeedEncryptedKey(TestDataFactory.CreateAddress(), Convert.ToBase64String(new byte[128]))
            },
            RotationTrigger: RotationTrigger.Join);
        var transaction = TestDataFactory.CreateKeyRotationSignedTransaction(payload, TestDataFactory.CreateAddress());

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithMultipleValidEncryptedKeys_ShouldSucceed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<GroupFeedKeyRotationContentHandler>();

        var feedId = TestDataFactory.CreateFeedId();
        var payload = TestDataFactory.CreateKeyRotationPayload(feedId, memberCount: 10);
        var transaction = TestDataFactory.CreateKeyRotationSignedTransaction(payload, TestDataFactory.CreateAddress());

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region FeedId and ValidFromBlock Validation Tests

    [Fact]
    public void ValidateAndSign_WithDefaultFeedId_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<GroupFeedKeyRotationContentHandler>();

        var payload = new GroupFeedKeyRotationPayload(
            default, // Invalid FeedId
            NewKeyGeneration: 2,
            PreviousKeyGeneration: 1,
            ValidFromBlock: 100,
            EncryptedKeys: new[]
            {
                new GroupFeedEncryptedKey(TestDataFactory.CreateAddress(), Convert.ToBase64String(new byte[128]))
            },
            RotationTrigger: RotationTrigger.Join);
        var transaction = TestDataFactory.CreateKeyRotationSignedTransaction(payload, TestDataFactory.CreateAddress());

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithZeroValidFromBlock_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<GroupFeedKeyRotationContentHandler>();

        var feedId = TestDataFactory.CreateFeedId();
        var payload = TestDataFactory.CreateKeyRotationPayload(feedId, validFromBlock: 0);
        var transaction = TestDataFactory.CreateKeyRotationSignedTransaction(payload, TestDataFactory.CreateAddress());

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithNegativeValidFromBlock_ShouldReturnNull()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<GroupFeedKeyRotationContentHandler>();

        var feedId = TestDataFactory.CreateFeedId();
        var payload = TestDataFactory.CreateKeyRotationPayload(feedId, validFromBlock: -1);
        var transaction = TestDataFactory.CreateKeyRotationSignedTransaction(payload, TestDataFactory.CreateAddress());

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithValidValidFromBlock_ShouldSucceed()
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<GroupFeedKeyRotationContentHandler>();

        var feedId = TestDataFactory.CreateFeedId();
        var payload = TestDataFactory.CreateKeyRotationPayload(feedId, validFromBlock: 12345);
        var transaction = TestDataFactory.CreateKeyRotationSignedTransaction(payload, TestDataFactory.CreateAddress());

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region Rotation Trigger Tests

    [Theory]
    [InlineData(RotationTrigger.Join)]
    [InlineData(RotationTrigger.Leave)]
    [InlineData(RotationTrigger.Ban)]
    [InlineData(RotationTrigger.Unban)]
    [InlineData(RotationTrigger.Manual)]
    public void ValidateAndSign_WithAllRotationTriggers_ShouldSucceed(RotationTrigger trigger)
    {
        // Arrange
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        var handler = mocker.CreateInstance<GroupFeedKeyRotationContentHandler>();

        var feedId = TestDataFactory.CreateFeedId();
        var payload = new GroupFeedKeyRotationPayload(
            feedId,
            NewKeyGeneration: 2,
            PreviousKeyGeneration: 1,
            ValidFromBlock: 100,
            EncryptedKeys: new[]
            {
                new GroupFeedEncryptedKey(TestDataFactory.CreateAddress(), Convert.ToBase64String(new byte[128]))
            },
            RotationTrigger: trigger);
        var transaction = TestDataFactory.CreateKeyRotationSignedTransaction(payload, TestDataFactory.CreateAddress());

        // Act
        var result = handler.ValidateAndSign(transaction);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion
}
