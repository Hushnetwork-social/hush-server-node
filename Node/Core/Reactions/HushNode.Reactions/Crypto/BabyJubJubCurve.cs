using System.Numerics;

namespace HushNode.Reactions.Crypto;

/// <summary>
/// Implementation of the Baby JubJub twisted Edwards curve.
/// Used for ElGamal encryption in Protocol Omega.
/// </summary>
public class BabyJubJubCurve : IBabyJubJub
{
    // Curve parameters for twisted Edwards curve: a*x² + y² = 1 + d*x²*y²
    private static readonly BigInteger A = 168700;
    private static readonly BigInteger D = 168696;

    // Field prime (same as BN254/BN128 scalar field)
    private static readonly BigInteger P = BigInteger.Parse(
        "21888242871839275222246405745257275088548364400416034343698204186575808495617");

    // Identity element for Twisted Edwards curves is (0, 1)
    private static readonly ECPoint _identity = new(BigInteger.Zero, BigInteger.One);

    // Generator point
    private static readonly ECPoint _generator = new(
        BigInteger.Parse("5299619240641551281634865583518297030282874472190772894086521144482721001553"),
        BigInteger.Parse("16950150798460657717958625567821834550301663161624707787222815936182638968203")
    );

    // Subgroup order
    private static readonly BigInteger _order = BigInteger.Parse(
        "2736030358979909402780800718157159386076813972158567259200215660948447373041");

    public ECPoint Generator => _generator;

    public BigInteger Order => _order;

    public ECPoint Identity => _identity;

    public ECPoint Add(ECPoint p1, ECPoint p2)
    {
        // Handle identity element (0, 1)
        if (p1.Equals(_identity)) return p2;
        if (p2.Equals(_identity)) return p1;

        // Twisted Edwards addition formula:
        // x3 = (x1*y2 + y1*x2) / (1 + d*x1*x2*y1*y2)
        // y3 = (y1*y2 - a*x1*x2) / (1 - d*x1*x2*y1*y2)

        var x1y2 = Mod(p1.X * p2.Y);
        var y1x2 = Mod(p1.Y * p2.X);
        var x1x2 = Mod(p1.X * p2.X);
        var y1y2 = Mod(p1.Y * p2.Y);
        var dx1x2y1y2 = Mod(D * Mod(x1x2 * y1y2));

        var x3Num = Mod(x1y2 + y1x2);
        var x3Den = Mod(1 + dx1x2y1y2);
        var x3 = Mod(x3Num * ModInverse(x3Den));

        var y3Num = Mod(y1y2 - Mod(A * x1x2) + P);
        var y3Den = Mod(1 - dx1x2y1y2 + P);
        var y3 = Mod(y3Num * ModInverse(y3Den));

        return new ECPoint(x3, y3);
    }

    public ECPoint Subtract(ECPoint p1, ECPoint p2)
    {
        // Negate p2 (negate X coordinate for twisted Edwards curves)
        var negP2 = new ECPoint(Mod(-p2.X + P), p2.Y);
        return Add(p1, negP2);
    }

    public ECPoint ScalarMul(ECPoint p, BigInteger scalar)
    {
        // Handle special cases
        if (scalar == BigInteger.Zero)
            return _identity;

        if (scalar < 0)
        {
            scalar = -scalar;
            p = new ECPoint(Mod(-p.X + P), p.Y);
        }

        // Double-and-add algorithm
        var result = _identity;
        var temp = p;

        while (scalar > 0)
        {
            if ((scalar & 1) == 1)
                result = Add(result, temp);
            temp = Add(temp, temp);
            scalar >>= 1;
        }

        return result;
    }

    public bool IsOnCurve(ECPoint p)
    {
        // Check: a*x² + y² = 1 + d*x²*y²
        var x2 = Mod(p.X * p.X);
        var y2 = Mod(p.Y * p.Y);
        var left = Mod(Mod(A * x2) + y2);
        var right = Mod(1 + Mod(Mod(D * x2) * y2));
        return left == right;
    }

    private static BigInteger Mod(BigInteger a)
    {
        var result = a % P;
        return result < 0 ? result + P : result;
    }

    private static BigInteger ModInverse(BigInteger a)
    {
        // Extended Euclidean Algorithm
        BigInteger m0 = P, x0 = 0, x1 = 1;

        if (P == 1) return 0;

        a = Mod(a);

        while (a > 1)
        {
            BigInteger q = a / m0;
            BigInteger t = m0;

            m0 = a % m0;
            a = t;
            t = x0;

            x0 = x1 - q * x0;
            x1 = t;
        }

        if (x1 < 0)
            x1 += P;

        return x1;
    }
}
