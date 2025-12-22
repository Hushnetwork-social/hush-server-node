using System.Numerics;
using FluentAssertions;
using HushNode.Reactions.Crypto;
using Xunit;

namespace HushNode.Reactions.Tests.Crypto;

/// <summary>
/// Tests for Poseidon hash function.
/// Poseidon is ZK-friendly and used for commitments and nullifiers.
/// </summary>
public class PoseidonHashTests
{
    private readonly PoseidonHash _poseidon;

    public PoseidonHashTests()
    {
        _poseidon = new PoseidonHash();
    }

    [Fact]
    public void Hash_EmptyInput_ShouldReturnConsistentResult()
    {
        var result1 = _poseidon.Hash();
        var result2 = _poseidon.Hash();

        result1.Should().Be(result2);
    }

    [Fact]
    public void Hash_SingleInput_ShouldReturnNonZero()
    {
        var input = BigInteger.Parse("12345");

        var result = _poseidon.Hash(input);

        result.Should().NotBe(BigInteger.Zero);
    }

    [Fact]
    public void Hash2_ShouldBeConsistent()
    {
        var a = BigInteger.Parse("111");
        var b = BigInteger.Parse("222");

        var result1 = _poseidon.Hash2(a, b);
        var result2 = _poseidon.Hash2(a, b);

        result1.Should().Be(result2);
    }

    [Fact]
    public void Hash2_DifferentInputs_ShouldProduceDifferentOutputs()
    {
        var a = BigInteger.Parse("111");
        var b = BigInteger.Parse("222");

        var result1 = _poseidon.Hash2(a, b);
        var result2 = _poseidon.Hash2(b, a);

        result1.Should().NotBe(result2);
    }

    [Fact]
    public void Hash4_ShouldBeConsistent()
    {
        var a = BigInteger.Parse("1");
        var b = BigInteger.Parse("2");
        var c = BigInteger.Parse("3");
        var d = BigInteger.Parse("4");

        var result1 = _poseidon.Hash4(a, b, c, d);
        var result2 = _poseidon.Hash4(a, b, c, d);

        result1.Should().Be(result2);
    }

    [Fact]
    public void Hash4_DifferentOrder_ShouldProduceDifferentOutputs()
    {
        var a = BigInteger.Parse("1");
        var b = BigInteger.Parse("2");
        var c = BigInteger.Parse("3");
        var d = BigInteger.Parse("4");

        var result1 = _poseidon.Hash4(a, b, c, d);
        var result2 = _poseidon.Hash4(d, c, b, a);

        result1.Should().NotBe(result2);
    }

    [Fact]
    public void Hash_ShouldBeInFieldRange()
    {
        // Result should be less than the BN254 scalar field prime
        var fieldPrime = BigInteger.Parse("21888242871839275222246405745257275088548364400416034343698204186575808495617");
        var input = BigInteger.Parse("999999999999999");

        var result = _poseidon.Hash(input);

        result.Should().BeGreaterOrEqualTo(BigInteger.Zero);
        result.Should().BeLessThan(fieldPrime);
    }

    [Fact]
    public void Hash2_LargeInputs_ShouldWork()
    {
        // Use field-sized inputs
        var fieldPrime = BigInteger.Parse("21888242871839275222246405745257275088548364400416034343698204186575808495617");
        var a = fieldPrime - 1;
        var b = fieldPrime - 2;

        var result = _poseidon.Hash2(a, b);

        result.Should().BeGreaterOrEqualTo(BigInteger.Zero);
        result.Should().BeLessThan(fieldPrime);
    }

    [Fact]
    public void Commitment_UserSecret_ShouldBeConsistent()
    {
        // Commitment = Poseidon(user_secret)
        var userSecret = BigInteger.Parse("123456789012345678901234567890");

        var commitment1 = _poseidon.Hash(userSecret);
        var commitment2 = _poseidon.Hash(userSecret);

        commitment1.Should().Be(commitment2);
    }

    [Fact]
    public void Nullifier_Computation_ShouldBeConsistent()
    {
        // Nullifier = Poseidon(user_secret, message_id, feed_id, DOMAIN)
        var userSecret = BigInteger.Parse("123456789");
        var messageId = BigInteger.Parse("987654321");
        var feedId = BigInteger.Parse("111222333");
        var domain = BigInteger.Parse("1"); // REACTION_DOMAIN

        var nullifier1 = _poseidon.Hash4(userSecret, messageId, feedId, domain);
        var nullifier2 = _poseidon.Hash4(userSecret, messageId, feedId, domain);

        nullifier1.Should().Be(nullifier2);
    }

    [Fact]
    public void Nullifier_DifferentMessage_ShouldBeDifferent()
    {
        var userSecret = BigInteger.Parse("123456789");
        var feedId = BigInteger.Parse("111222333");
        var domain = BigInteger.Parse("1");

        var messageId1 = BigInteger.Parse("100");
        var messageId2 = BigInteger.Parse("200");

        var nullifier1 = _poseidon.Hash4(userSecret, messageId1, feedId, domain);
        var nullifier2 = _poseidon.Hash4(userSecret, messageId2, feedId, domain);

        nullifier1.Should().NotBe(nullifier2);
    }

    [Fact]
    public void Nullifier_DifferentUser_ShouldBeDifferent()
    {
        var messageId = BigInteger.Parse("987654321");
        var feedId = BigInteger.Parse("111222333");
        var domain = BigInteger.Parse("1");

        var userSecret1 = BigInteger.Parse("100");
        var userSecret2 = BigInteger.Parse("200");

        var nullifier1 = _poseidon.Hash4(userSecret1, messageId, feedId, domain);
        var nullifier2 = _poseidon.Hash4(userSecret2, messageId, feedId, domain);

        nullifier1.Should().NotBe(nullifier2);
    }

    [Fact]
    public void MerkleTreeHash_ShouldWork()
    {
        // Merkle tree uses Hash2 for combining sibling nodes
        var left = BigInteger.Parse("111");
        var right = BigInteger.Parse("222");

        var parent = _poseidon.Hash2(left, right);

        parent.Should().NotBe(BigInteger.Zero);
        parent.Should().NotBe(left);
        parent.Should().NotBe(right);
    }

    [Fact]
    public void MerkleTreeHash_ZeroLeaves_ShouldBeConsistent()
    {
        // Empty leaves are represented as 0
        var zero = BigInteger.Zero;

        var level1 = _poseidon.Hash2(zero, zero);
        var level2 = _poseidon.Hash2(level1, level1);
        var level3 = _poseidon.Hash2(level2, level2);

        // Results should be consistent
        level1.Should().NotBe(BigInteger.Zero);
        level2.Should().NotBe(level1);
        level3.Should().NotBe(level2);
    }

    [Fact]
    public void Hash_ZeroInput_ShouldNotBeZero()
    {
        var result = _poseidon.Hash(BigInteger.Zero);

        // Hash of zero should not be zero (would be a weak hash)
        result.Should().NotBe(BigInteger.Zero);
    }
}
