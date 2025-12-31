using FluentAssertions;
using HushNode.Credentials;
using HushNode.Reactions.Crypto;
using Microsoft.Extensions.Options;
using Xunit;

namespace HushNode.Reactions.Tests.Services;

/// <summary>
/// Tests for UserCommitmentService - commitment derivation from addresses and private keys.
/// </summary>
public class UserCommitmentServiceTests
{
    [Fact]
    public void DeriveCommitmentFromAddress_ReturnsDeterministicCommitment()
    {
        // Arrange
        var poseidon = new PoseidonHash();
        var credentials = Options.Create(new CredentialsProfile
        {
            ProfileName = "Test",
            PrivateSigningKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            PublicSigningAddress = "test-address",
            PrivateEncryptKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            PublicEncryptAddress = "test-encrypt-address"
        });

        var service = new UserCommitmentService(credentials, poseidon);

        var address = "04abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";

        // Act
        var commitment1 = service.DeriveCommitmentFromAddress(address);
        var commitment2 = service.DeriveCommitmentFromAddress(address);

        // Assert
        commitment1.Should().BeEquivalentTo(commitment2, "same address should produce same commitment");
        commitment1.Length.Should().Be(32, "commitment should be 32 bytes");
    }

    [Fact]
    public void DeriveCommitmentFromAddress_DifferentAddresses_ProduceDifferentCommitments()
    {
        // Arrange
        var poseidon = new PoseidonHash();
        var credentials = Options.Create(new CredentialsProfile
        {
            ProfileName = "Test",
            PrivateSigningKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            PublicSigningAddress = "test-address",
            PrivateEncryptKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            PublicEncryptAddress = "test-encrypt-address"
        });

        var service = new UserCommitmentService(credentials, poseidon);

        var address1 = "04abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";
        var address2 = "04fedcba0987654321fedcba0987654321fedcba0987654321fedcba0987654321";

        // Act
        var commitment1 = service.DeriveCommitmentFromAddress(address1);
        var commitment2 = service.DeriveCommitmentFromAddress(address2);

        // Assert
        commitment1.Should().NotBeEquivalentTo(commitment2, "different addresses should produce different commitments");
    }

    [Fact]
    public void GetLocalUserCommitment_ReturnsValidCommitment()
    {
        // Arrange
        var poseidon = new PoseidonHash();
        var credentials = Options.Create(new CredentialsProfile
        {
            ProfileName = "Test",
            PrivateSigningKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            PublicSigningAddress = "test-address",
            PrivateEncryptKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            PublicEncryptAddress = "test-encrypt-address"
        });

        var service = new UserCommitmentService(credentials, poseidon);

        // Act
        var commitment = service.GetLocalUserCommitment();

        // Assert
        commitment.Should().NotBeNull();
        commitment.Length.Should().Be(32, "commitment should be 32 bytes");
    }

    [Fact]
    public void GetLocalUserCommitment_IsDeterministic()
    {
        // Arrange
        var poseidon = new PoseidonHash();
        var credentials = Options.Create(new CredentialsProfile
        {
            ProfileName = "Test",
            PrivateSigningKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            PublicSigningAddress = "test-address",
            PrivateEncryptKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            PublicEncryptAddress = "test-encrypt-address"
        });

        var service1 = new UserCommitmentService(credentials, poseidon);
        var service2 = new UserCommitmentService(credentials, poseidon);

        // Act
        var commitment1 = service1.GetLocalUserCommitment();
        var commitment2 = service2.GetLocalUserCommitment();

        // Assert
        commitment1.Should().BeEquivalentTo(commitment2, "same private key should produce same commitment");
    }

    [Fact]
    public void GetLocalUserSecret_ReturnsNonZeroValue()
    {
        // Arrange
        var poseidon = new PoseidonHash();
        var credentials = Options.Create(new CredentialsProfile
        {
            ProfileName = "Test",
            PrivateSigningKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            PublicSigningAddress = "test-address",
            PrivateEncryptKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            PublicEncryptAddress = "test-encrypt-address"
        });

        var service = new UserCommitmentService(credentials, poseidon);

        // Act
        var secret = service.GetLocalUserSecret();

        // Assert
        secret.Should().NotBe(System.Numerics.BigInteger.Zero, "secret should not be zero");
        secret.Sign.Should().Be(1, "secret should be positive");
    }
}
