using System.Numerics;
using FluentAssertions;
using HushShared.Reactions.Model;
using Xunit;

namespace HushNode.Reactions.Tests.Crypto;

/// <summary>
/// Tests for ECPoint serialization and equality.
/// </summary>
public class ECPointTests
{
    [Fact]
    public void Constructor_ShouldSetCoordinates()
    {
        var x = new BigInteger(123);
        var y = new BigInteger(456);

        var point = new ECPoint(x, y);

        point.X.Should().Be(x);
        point.Y.Should().Be(y);
    }

    [Fact]
    public void ToBytes_ShouldReturn64Bytes()
    {
        var point = new ECPoint(BigInteger.One, BigInteger.One);

        var bytes = point.ToBytes();

        bytes.Length.Should().Be(64);
    }

    [Fact]
    public void ToBytes_FromBytes_ShouldRoundTrip()
    {
        var original = new ECPoint(
            BigInteger.Parse("12345678901234567890"),
            BigInteger.Parse("98765432109876543210"));

        var bytes = original.ToBytes();
        var restored = ECPoint.FromBytes(bytes);

        restored.X.Should().Be(original.X);
        restored.Y.Should().Be(original.Y);
    }

    [Fact]
    public void FromBytes_InvalidLength_ShouldThrow()
    {
        var invalidBytes = new byte[32]; // Should be 64

        var act = () => ECPoint.FromBytes(invalidBytes);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*64*");
    }

    [Fact]
    public void FromCoordinates_ShouldCreatePoint()
    {
        var xBytes = new byte[32];
        var yBytes = new byte[32];
        xBytes[31] = 1; // x = 1
        yBytes[31] = 2; // y = 2

        var point = ECPoint.FromCoordinates(xBytes, yBytes);

        point.X.Should().Be(BigInteger.One);
        point.Y.Should().Be(new BigInteger(2));
    }

    [Fact]
    public void Equals_SameCoordinates_ShouldBeTrue()
    {
        var point1 = new ECPoint(BigInteger.One, BigInteger.One);
        var point2 = new ECPoint(BigInteger.One, BigInteger.One);

        point1.Equals(point2).Should().BeTrue();
        (point1 == point2).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentCoordinates_ShouldBeFalse()
    {
        var point1 = new ECPoint(BigInteger.One, BigInteger.One);
        var point2 = new ECPoint(BigInteger.One, new BigInteger(2));

        point1.Equals(point2).Should().BeFalse();
        (point1 != point2).Should().BeTrue();
    }

    [Fact]
    public void Equals_Null_ShouldBeFalse()
    {
        var point = new ECPoint(BigInteger.One, BigInteger.One);

        point.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_SamePoints_ShouldBeSame()
    {
        var point1 = new ECPoint(BigInteger.One, BigInteger.One);
        var point2 = new ECPoint(BigInteger.One, BigInteger.One);

        point1.GetHashCode().Should().Be(point2.GetHashCode());
    }

    [Fact]
    public void ToString_ShouldShowCoordinates()
    {
        var point = new ECPoint(new BigInteger(123), new BigInteger(456));

        var str = point.ToString();

        str.Should().Contain("123");
        str.Should().Contain("456");
    }

    [Fact]
    public void ToBytes_LargeCoordinates_ShouldPadCorrectly()
    {
        // Use large field-sized coordinates
        var fieldPrime = BigInteger.Parse("21888242871839275222246405745257275088548364400416034343698204186575808495617");
        var x = fieldPrime - 1;
        var y = fieldPrime - 2;

        var point = new ECPoint(x, y);
        var bytes = point.ToBytes();
        var restored = ECPoint.FromBytes(bytes);

        restored.X.Should().Be(x);
        restored.Y.Should().Be(y);
    }

    [Fact]
    public void ToBytes_ZeroCoordinates_ShouldWork()
    {
        var point = new ECPoint(BigInteger.Zero, BigInteger.One);

        var bytes = point.ToBytes();
        var restored = ECPoint.FromBytes(bytes);

        restored.X.Should().Be(BigInteger.Zero);
        restored.Y.Should().Be(BigInteger.One);
    }

    [Fact]
    public void OperatorEquals_BothNull_ShouldBeTrue()
    {
        ECPoint? point1 = null;
        ECPoint? point2 = null;

        (point1 == point2).Should().BeTrue();
    }

    [Fact]
    public void OperatorEquals_OneNull_ShouldBeFalse()
    {
        ECPoint? point1 = new ECPoint(BigInteger.One, BigInteger.One);
        ECPoint? point2 = null;

        (point1 == point2).Should().BeFalse();
        (point2 == point1).Should().BeFalse();
    }
}
