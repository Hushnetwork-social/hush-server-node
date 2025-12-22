using System.Numerics;

namespace HushNode.Reactions.Crypto;

/// <summary>
/// Interface for Baby JubJub elliptic curve operations.
/// </summary>
public interface IBabyJubJub
{
    /// <summary>
    /// The generator point of the curve.
    /// </summary>
    ECPoint Generator { get; }

    /// <summary>
    /// The order of the curve's subgroup.
    /// </summary>
    BigInteger Order { get; }

    /// <summary>
    /// The identity element (0, 1) for Twisted Edwards curves.
    /// </summary>
    ECPoint Identity { get; }

    /// <summary>
    /// Adds two points on the curve.
    /// </summary>
    ECPoint Add(ECPoint p1, ECPoint p2);

    /// <summary>
    /// Subtracts p2 from p1 on the curve.
    /// </summary>
    ECPoint Subtract(ECPoint p1, ECPoint p2);

    /// <summary>
    /// Multiplies a point by a scalar.
    /// </summary>
    ECPoint ScalarMul(ECPoint p, BigInteger scalar);

    /// <summary>
    /// Checks if a point is on the curve.
    /// </summary>
    bool IsOnCurve(ECPoint p);
}
