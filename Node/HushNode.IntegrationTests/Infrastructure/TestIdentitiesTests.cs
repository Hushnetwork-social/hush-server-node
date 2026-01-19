using FluentAssertions;
using HushServerNode.Testing;
using Xunit;

namespace HushNode.IntegrationTests.Tests;

/// <summary>
/// Unit tests for TestIdentities to verify key generation and determinism within a test run.
/// </summary>
public class TestIdentitiesTests
{
    [Fact]
    public void Alice_ShouldReturnNonNullIdentity()
    {
        // Act
        var alice = TestIdentities.Alice;

        // Assert
        alice.Should().NotBeNull();
        alice.DisplayName.Should().Be("Alice");
    }

    [Fact]
    public void Alice_MultipleCalls_ShouldReturnSameInstance()
    {
        // Act
        var alice1 = TestIdentities.Alice;
        var alice2 = TestIdentities.Alice;

        // Assert
        alice1.Should().BeSameAs(alice2);
    }

    [Fact]
    public void BlockProducer_ShouldReturnNonNullIdentity()
    {
        // Act
        var blockProducer = TestIdentities.BlockProducer;

        // Assert
        blockProducer.Should().NotBeNull();
        blockProducer.DisplayName.Should().Be("BlockProducer");
    }

    [Fact]
    public void AllIdentities_ShouldHaveUniquePublicSigningAddresses()
    {
        // Arrange
        var identities = new[]
        {
            TestIdentities.Alice,
            TestIdentities.Bob,
            TestIdentities.Charlie,
            TestIdentities.BlockProducer
        };

        // Act
        var publicAddresses = identities.Select(i => i.PublicSigningAddress).ToList();

        // Assert
        publicAddresses.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void AllIdentities_ShouldHaveValidKeyPairs()
    {
        // Arrange
        var identities = new[]
        {
            TestIdentities.Alice,
            TestIdentities.Bob,
            TestIdentities.Charlie,
            TestIdentities.BlockProducer
        };

        // Assert
        foreach (var identity in identities)
        {
            identity.PublicSigningAddress.Should().NotBeNullOrWhiteSpace();
            identity.PrivateSigningKey.Should().NotBeNullOrWhiteSpace();
            identity.PublicEncryptAddress.Should().NotBeNullOrWhiteSpace();
            identity.PrivateEncryptKey.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void ToCredentialsProfile_ShouldCreateValidProfile()
    {
        // Arrange
        var alice = TestIdentities.Alice;

        // Act
        var profile = alice.ToCredentialsProfile();

        // Assert
        profile.Should().NotBeNull();
        profile.ProfileName.Should().Be("Alice");
        profile.PublicSigningAddress.Should().Be(alice.PublicSigningAddress);
        profile.PrivateSigningKey.Should().Be(alice.PrivateSigningKey);
        profile.PublicEncryptAddress.Should().Be(alice.PublicEncryptAddress);
        profile.PrivateEncryptKey.Should().Be(alice.PrivateEncryptKey);
        profile.IsPublic.Should().BeFalse();
    }

    [Fact]
    public void GenerateFromSeed_WithSameSeed_ShouldReturnNewInstanceEachCall()
    {
        // Arrange & Act
        var identity1 = TestIdentities.GenerateFromSeed("TEST_SEED", "Test");
        var identity2 = TestIdentities.GenerateFromSeed("TEST_SEED", "Test");

        // Assert - Each call generates new keys (not cached like the static properties)
        identity1.Should().NotBeSameAs(identity2);
    }

    [Fact]
    public void GenerateFromSeed_ShouldReturnIdentityWithCorrectDisplayName()
    {
        // Arrange & Act
        var identity = TestIdentities.GenerateFromSeed("CUSTOM_SEED", "CustomUser");

        // Assert
        identity.DisplayName.Should().Be("CustomUser");
        identity.Seed.Should().Be("CUSTOM_SEED");
    }
}
