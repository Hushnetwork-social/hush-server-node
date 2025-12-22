using System.Numerics;
using FluentAssertions;
using HushNode.Reactions.Crypto;
using HushShared.Reactions.Model;
using Xunit;

namespace HushNode.Reactions.Tests.Crypto;

/// <summary>
/// Tests for Baby JubJub elliptic curve operations.
/// These are critical for homomorphic tally operations.
/// </summary>
public class BabyJubJubCurveTests
{
    private readonly BabyJubJubCurve _curve;

    public BabyJubJubCurveTests()
    {
        _curve = new BabyJubJubCurve();
    }

    [Fact]
    public void Identity_ShouldBeZeroOne()
    {
        // The identity point on Baby JubJub is (0, 1)
        var identity = _curve.Identity;

        identity.X.Should().Be(BigInteger.Zero);
        identity.Y.Should().Be(BigInteger.One);
    }

    [Fact]
    public void Identity_ShouldBeOnCurve()
    {
        var identity = _curve.Identity;

        _curve.IsOnCurve(identity).Should().BeTrue();
    }

    [Fact]
    public void Add_IdentityToPoint_ShouldReturnSamePoint()
    {
        // P + O = P (identity is additive identity)
        var identity = _curve.Identity;
        var point = CreateTestPoint();

        var result = _curve.Add(point, identity);

        result.X.Should().Be(point.X);
        result.Y.Should().Be(point.Y);
    }

    [Fact]
    public void Add_PointToIdentity_ShouldReturnSamePoint()
    {
        // O + P = P
        var identity = _curve.Identity;
        var point = CreateTestPoint();

        var result = _curve.Add(identity, point);

        result.X.Should().Be(point.X);
        result.Y.Should().Be(point.Y);
    }

    [Fact]
    public void Add_PointToItself_ShouldDouble()
    {
        // P + P = 2P
        var point = CreateTestPoint();

        var doubled = _curve.Add(point, point);
        var scalared = _curve.ScalarMul(point, 2);

        doubled.X.Should().Be(scalared.X);
        doubled.Y.Should().Be(scalared.Y);
    }

    [Fact]
    public void Subtract_PointFromItself_ShouldGiveIdentity()
    {
        // P - P = O
        var point = CreateTestPoint();

        var result = _curve.Subtract(point, point);

        result.X.Should().Be(_curve.Identity.X);
        result.Y.Should().Be(_curve.Identity.Y);
    }

    [Fact]
    public void Subtract_IdentityFromPoint_ShouldReturnSamePoint()
    {
        // P - O = P
        var point = CreateTestPoint();
        var identity = _curve.Identity;

        var result = _curve.Subtract(point, identity);

        result.X.Should().Be(point.X);
        result.Y.Should().Be(point.Y);
    }

    [Fact]
    public void Add_ThenSubtract_ShouldReturnOriginal()
    {
        // (P + Q) - Q = P
        var p = CreateTestPoint();
        var q = _curve.ScalarMul(CreateTestPoint(), 3);

        var sum = _curve.Add(p, q);
        var result = _curve.Subtract(sum, q);

        result.X.Should().Be(p.X);
        result.Y.Should().Be(p.Y);
    }

    [Fact]
    public void ScalarMul_ByZero_ShouldGiveIdentity()
    {
        // 0 * P = O
        var point = CreateTestPoint();

        var result = _curve.ScalarMul(point, BigInteger.Zero);

        result.X.Should().Be(_curve.Identity.X);
        result.Y.Should().Be(_curve.Identity.Y);
    }

    [Fact]
    public void ScalarMul_ByOne_ShouldReturnSamePoint()
    {
        // 1 * P = P
        var point = CreateTestPoint();

        var result = _curve.ScalarMul(point, BigInteger.One);

        result.X.Should().Be(point.X);
        result.Y.Should().Be(point.Y);
    }

    [Fact]
    public void ScalarMul_IsDistributive()
    {
        // (a + b) * P = a*P + b*P
        var point = CreateTestPoint();
        var a = new BigInteger(5);
        var b = new BigInteger(7);

        var left = _curve.ScalarMul(point, a + b);
        var right = _curve.Add(
            _curve.ScalarMul(point, a),
            _curve.ScalarMul(point, b));

        left.X.Should().Be(right.X);
        left.Y.Should().Be(right.Y);
    }

    [Fact]
    public void Add_IsCommutative()
    {
        // P + Q = Q + P
        var p = CreateTestPoint();
        var q = _curve.ScalarMul(CreateTestPoint(), 5);

        var pq = _curve.Add(p, q);
        var qp = _curve.Add(q, p);

        pq.X.Should().Be(qp.X);
        pq.Y.Should().Be(qp.Y);
    }

    [Fact]
    public void Add_IsAssociative()
    {
        // (P + Q) + R = P + (Q + R)
        var p = CreateTestPoint();
        var q = _curve.ScalarMul(CreateTestPoint(), 3);
        var r = _curve.ScalarMul(CreateTestPoint(), 7);

        var left = _curve.Add(_curve.Add(p, q), r);
        var right = _curve.Add(p, _curve.Add(q, r));

        left.X.Should().Be(right.X);
        left.Y.Should().Be(right.Y);
    }

    [Fact]
    public void IsOnCurve_ValidPoint_ShouldReturnTrue()
    {
        var point = CreateTestPoint();

        _curve.IsOnCurve(point).Should().BeTrue();
    }

    [Fact]
    public void IsOnCurve_InvalidPoint_ShouldReturnFalse()
    {
        // Random point not on curve
        var invalidPoint = new ECPoint(
            BigInteger.Parse("12345"),
            BigInteger.Parse("67890"));

        _curve.IsOnCurve(invalidPoint).Should().BeFalse();
    }

    [Fact]
    public void ScalarMul_ResultShouldBeOnCurve()
    {
        var point = CreateTestPoint();
        var scalar = new BigInteger(12345);

        var result = _curve.ScalarMul(point, scalar);

        _curve.IsOnCurve(result).Should().BeTrue();
    }

    [Fact]
    public void Add_ResultShouldBeOnCurve()
    {
        var p = CreateTestPoint();
        var q = _curve.ScalarMul(CreateTestPoint(), 7);

        var result = _curve.Add(p, q);

        _curve.IsOnCurve(result).Should().BeTrue();
    }

    [Fact]
    public void HomomorphicTallyOperation_ShouldWork()
    {
        // Simulate: tally = vote1 + vote2 + vote3
        // Then update: tally = tally - vote2 + vote2_new
        var vote1 = _curve.ScalarMul(CreateTestPoint(), 1);
        var vote2 = _curve.ScalarMul(CreateTestPoint(), 2);
        var vote3 = _curve.ScalarMul(CreateTestPoint(), 3);
        var vote2New = _curve.ScalarMul(CreateTestPoint(), 5);

        // Initial tally
        var tally = _curve.Add(_curve.Add(vote1, vote2), vote3);

        // Update: remove old vote2, add new vote2
        var updatedTally = _curve.Add(_curve.Subtract(tally, vote2), vote2New);

        // Expected: vote1 + vote3 + vote2_new
        var expected = _curve.Add(_curve.Add(vote1, vote3), vote2New);

        updatedTally.X.Should().Be(expected.X);
        updatedTally.Y.Should().Be(expected.Y);
    }

    /// <summary>
    /// Creates a valid test point on the Baby JubJub curve.
    /// Uses the generator point G = (Gx, Gy).
    /// </summary>
    private ECPoint CreateTestPoint()
    {
        // Baby JubJub generator point
        var gx = BigInteger.Parse("5299619240641551281634865583518297030282874472190772894086521144482721001553");
        var gy = BigInteger.Parse("16950150798460657717958625567821834550301663161624707787222815936182638968203");
        return new ECPoint(gx, gy);
    }
}
