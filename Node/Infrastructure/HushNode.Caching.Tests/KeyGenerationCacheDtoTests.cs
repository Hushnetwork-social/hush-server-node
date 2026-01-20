using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace HushNode.Caching.Tests;

/// <summary>
/// Tests for KeyGenerationCacheDto and CachedKeyGenerations serialization.
/// Verifies JSON roundtrip and null handling for cache storage.
/// </summary>
public class KeyGenerationCacheDtoTests
{
    #region KeyGenerationCacheDto Serialization Tests

    [Fact]
    public void KeyGenerationCacheDto_SerializeAndDeserialize_RoundtripSucceeds()
    {
        // Arrange
        var dto = new KeyGenerationCacheDto
        {
            Version = 1,
            ValidFromBlock = 100,
            ValidToBlock = 149,
            EncryptedKeysByMember = new Dictionary<string, string>
            {
                ["alice-address"] = "encrypted-key-for-alice",
                ["bob-address"] = "encrypted-key-for-bob"
            }
        };

        // Act
        var json = JsonSerializer.Serialize(dto);
        var deserialized = JsonSerializer.Deserialize<KeyGenerationCacheDto>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Version.Should().Be(1);
        deserialized.ValidFromBlock.Should().Be(100);
        deserialized.ValidToBlock.Should().Be(149);
        deserialized.EncryptedKeysByMember.Should().HaveCount(2);
        deserialized.EncryptedKeysByMember["alice-address"].Should().Be("encrypted-key-for-alice");
        deserialized.EncryptedKeysByMember["bob-address"].Should().Be("encrypted-key-for-bob");
    }

    [Fact]
    public void KeyGenerationCacheDto_WithNullValidToBlock_SerializesCorrectly()
    {
        // Arrange
        var dto = new KeyGenerationCacheDto
        {
            Version = 2,
            ValidFromBlock = 150,
            ValidToBlock = null, // Active generation has no end block
            EncryptedKeysByMember = new Dictionary<string, string>
            {
                ["user-address"] = "encrypted-key"
            }
        };

        // Act
        var json = JsonSerializer.Serialize(dto);
        var deserialized = JsonSerializer.Deserialize<KeyGenerationCacheDto>(json);

        // Assert
        json.Should().Contain("\"validToBlock\":null");
        deserialized.Should().NotBeNull();
        deserialized!.ValidToBlock.Should().BeNull();
    }

    [Fact]
    public void KeyGenerationCacheDto_WithEmptyEncryptedKeys_SerializesCorrectly()
    {
        // Arrange
        var dto = new KeyGenerationCacheDto
        {
            Version = 1,
            ValidFromBlock = 100,
            ValidToBlock = 149,
            EncryptedKeysByMember = new Dictionary<string, string>()
        };

        // Act
        var json = JsonSerializer.Serialize(dto);
        var deserialized = JsonSerializer.Deserialize<KeyGenerationCacheDto>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.EncryptedKeysByMember.Should().BeEmpty();
    }

    #endregion

    #region CachedKeyGenerations Serialization Tests

    [Fact]
    public void CachedKeyGenerations_SerializeAndDeserialize_RoundtripSucceeds()
    {
        // Arrange
        var cached = new CachedKeyGenerations
        {
            KeyGenerations = new List<KeyGenerationCacheDto>
            {
                new()
                {
                    Version = 1,
                    ValidFromBlock = 100,
                    ValidToBlock = 149,
                    EncryptedKeysByMember = new Dictionary<string, string>
                    {
                        ["alice"] = "key1"
                    }
                },
                new()
                {
                    Version = 2,
                    ValidFromBlock = 150,
                    ValidToBlock = null,
                    EncryptedKeysByMember = new Dictionary<string, string>
                    {
                        ["alice"] = "key2",
                        ["bob"] = "key2-bob"
                    }
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(cached);
        var deserialized = JsonSerializer.Deserialize<CachedKeyGenerations>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.KeyGenerations.Should().HaveCount(2);
        deserialized.KeyGenerations[0].Version.Should().Be(1);
        deserialized.KeyGenerations[0].ValidToBlock.Should().Be(149);
        deserialized.KeyGenerations[1].Version.Should().Be(2);
        deserialized.KeyGenerations[1].ValidToBlock.Should().BeNull();
    }

    [Fact]
    public void CachedKeyGenerations_WithEmptyList_SerializesCorrectly()
    {
        // Arrange
        var cached = new CachedKeyGenerations
        {
            KeyGenerations = new List<KeyGenerationCacheDto>()
        };

        // Act
        var json = JsonSerializer.Serialize(cached);
        var deserialized = JsonSerializer.Deserialize<CachedKeyGenerations>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.KeyGenerations.Should().BeEmpty();
    }

    #endregion

    #region FeedParticipantsCacheConstants Tests

    [Fact]
    public void GetParticipantsKey_ReturnsCorrectFormat()
    {
        // Arrange
        var feedId = "abc-123-def";

        // Act
        var key = FeedParticipantsCacheConstants.GetParticipantsKey(feedId);

        // Assert
        key.Should().Be("feed:abc-123-def:participants");
    }

    [Fact]
    public void GetKeyGenerationsKey_ReturnsCorrectFormat()
    {
        // Arrange
        var feedId = "abc-123-def";

        // Act
        var key = FeedParticipantsCacheConstants.GetKeyGenerationsKey(feedId);

        // Assert
        key.Should().Be("feed:abc-123-def:keys");
    }

    [Fact]
    public void CacheTtl_IsOneHour()
    {
        // Assert
        FeedParticipantsCacheConstants.CacheTtl.Should().Be(TimeSpan.FromHours(1));
    }

    #endregion
}
