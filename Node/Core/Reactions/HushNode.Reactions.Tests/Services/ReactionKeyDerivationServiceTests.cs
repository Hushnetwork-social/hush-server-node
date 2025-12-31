using FluentAssertions;
using HushNode.Reactions.Tests.Fixtures;
using Xunit;

namespace HushNode.Reactions.Tests.Services;

/// <summary>
/// Tests for ReactionKeyDerivationService - HKDF-based key derivation for Group Feed reactions.
/// </summary>
public class ReactionKeyDerivationServiceTests
{
    private readonly ReactionKeyDerivationService _service;

    public ReactionKeyDerivationServiceTests()
    {
        _service = new ReactionKeyDerivationService();
    }

    [Fact]
    public void DeriveReactionKey_UniquePerMessage()
    {
        // Arrange
        var groupAesKey = new byte[32];
        new Random(42).NextBytes(groupAesKey);
        var messageId1 = TestDataFactory.CreateMessageId();
        var messageId2 = TestDataFactory.CreateMessageId();

        // Act
        var key1 = _service.DeriveReactionKey(groupAesKey, messageId1);
        var key2 = _service.DeriveReactionKey(groupAesKey, messageId2);

        // Assert
        key1.Should().NotBeEquivalentTo(key2, "different messages should have different reaction keys");
        key1.Length.Should().Be(32, "derived key should be 32 bytes");
        key2.Length.Should().Be(32, "derived key should be 32 bytes");
    }

    [Fact]
    public void DeriveReactionKey_DeterministicWithSameInputs()
    {
        // Arrange
        var groupAesKey = new byte[32];
        new Random(42).NextBytes(groupAesKey);
        var messageId = TestDataFactory.CreateMessageId();

        // Act
        var key1 = _service.DeriveReactionKey(groupAesKey, messageId);
        var key2 = _service.DeriveReactionKey(groupAesKey, messageId);

        // Assert
        key1.Should().BeEquivalentTo(key2, "same inputs should produce same key");
    }

    [Fact]
    public void DeriveFeedSecret_ConsistentForSameFeed()
    {
        // Arrange
        var groupAesKey = new byte[32];
        new Random(42).NextBytes(groupAesKey);
        var feedId = TestDataFactory.CreateFeedId();

        // Act
        var secret1 = _service.DeriveFeedSecret(groupAesKey, feedId);
        var secret2 = _service.DeriveFeedSecret(groupAesKey, feedId);

        // Assert
        secret1.Should().BeEquivalentTo(secret2, "same feed should produce same secret");
        secret1.Length.Should().Be(32, "derived secret should be 32 bytes");
    }

    [Fact]
    public void DeriveFeedSecret_DifferentForDifferentFeeds()
    {
        // Arrange
        var groupAesKey = new byte[32];
        new Random(42).NextBytes(groupAesKey);
        var feedId1 = TestDataFactory.CreateFeedId();
        var feedId2 = TestDataFactory.CreateFeedId();

        // Act
        var secret1 = _service.DeriveFeedSecret(groupAesKey, feedId1);
        var secret2 = _service.DeriveFeedSecret(groupAesKey, feedId2);

        // Assert
        secret1.Should().NotBeEquivalentTo(secret2, "different feeds should have different secrets");
    }

    [Fact]
    public void DeriveReactionKey_DifferentWithDifferentGroupKeys()
    {
        // Arrange
        var groupAesKey1 = new byte[32];
        var groupAesKey2 = new byte[32];
        new Random(42).NextBytes(groupAesKey1);
        new Random(99).NextBytes(groupAesKey2);
        var messageId = TestDataFactory.CreateMessageId();

        // Act
        var key1 = _service.DeriveReactionKey(groupAesKey1, messageId);
        var key2 = _service.DeriveReactionKey(groupAesKey2, messageId);

        // Assert
        key1.Should().NotBeEquivalentTo(key2, "different group keys should produce different reaction keys");
    }

    [Fact]
    public void DeriveFeedSecret_DifferentWithDifferentGroupKeys()
    {
        // Arrange
        var groupAesKey1 = new byte[32];
        var groupAesKey2 = new byte[32];
        new Random(42).NextBytes(groupAesKey1);
        new Random(99).NextBytes(groupAesKey2);
        var feedId = TestDataFactory.CreateFeedId();

        // Act
        var secret1 = _service.DeriveFeedSecret(groupAesKey1, feedId);
        var secret2 = _service.DeriveFeedSecret(groupAesKey2, feedId);

        // Assert
        secret1.Should().NotBeEquivalentTo(secret2, "different group keys should produce different feed secrets");
    }
}
