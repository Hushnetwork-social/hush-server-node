using FluentAssertions;
using HushNode.Credentials;
using HushNode.Reactions.Crypto;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
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
    public void DeriveCommitmentFromAddress_KnownFollowerAAddress_MatchesBrowserVector()
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
        var address = "029a54d5a2a235fca4117dc88caebf22eac3fc9e817231fcb62b8901a76cc47fde";

        // Act
        var commitment = service.DeriveCommitmentFromAddress(address);

        // Assert
        Convert.ToBase64String(commitment)
            .Should()
            .Be("IwNuigGoOEe4Z5yNAqOUQ97lokUkO224CMarpsgyf6Q=");
    }

    [Fact]
    public void DeriveAddressSecret_KnownFollowerAAddress_MatchesBrowserVector()
    {
        var address = "029a54d5a2a235fca4117dc88caebf22eac3fc9e817231fcb62b8901a76cc47fde";
        var salt = Encoding.UTF8.GetBytes("hush-network-address-commitment");
        var info = Encoding.UTF8.GetBytes("address-secret-v1");
        var ikm = Encoding.UTF8.GetBytes(address);
        var okm = DeriveHkdfSha256(ikm, 32, salt, info);
        var curveOrder = System.Numerics.BigInteger.Parse(
            "21888242871839275222246405745257275088614511777268538073601725287587578984328");
        var secret = new System.Numerics.BigInteger(okm, isUnsigned: true, isBigEndian: true) % curveOrder;
        if (secret == System.Numerics.BigInteger.Zero)
        {
            secret = System.Numerics.BigInteger.One;
        }

        Convert.ToHexString(secret.ToByteArray(isUnsigned: true, isBigEndian: true)).ToLowerInvariant().PadLeft(64, '0')
            .Should()
            .Be("2afd31bc1328e066e36266ddf5c30504a0ae148e7ae31048be09bccff17b86f6");
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

    private static byte[] DeriveHkdfSha256(byte[] inputKeyMaterial, int outputLength, byte[] salt, byte[] info)
    {
        using var extract = new HMACSHA256(salt);
        var prk = extract.ComputeHash(inputKeyMaterial);

        var okm = new byte[outputLength];
        var previousBlock = Array.Empty<byte>();
        byte counter = 1;
        var offset = 0;

        while (offset < outputLength)
        {
            using var expand = new HMACSHA256(prk);
            var input = new byte[previousBlock.Length + info.Length + 1];
            Buffer.BlockCopy(previousBlock, 0, input, 0, previousBlock.Length);
            Buffer.BlockCopy(info, 0, input, previousBlock.Length, info.Length);
            input[^1] = counter;

            previousBlock = expand.ComputeHash(input);
            var bytesToCopy = Math.Min(previousBlock.Length, outputLength - offset);
            Buffer.BlockCopy(previousBlock, 0, okm, offset, bytesToCopy);
            offset += bytesToCopy;
            counter++;
        }

        return okm;
    }
}
