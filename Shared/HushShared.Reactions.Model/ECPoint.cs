using System.Numerics;

namespace HushShared.Reactions.Model;

/// <summary>
/// Represents a point on an elliptic curve (Baby JubJub).
/// </summary>
public class ECPoint
{
    public BigInteger X { get; }
    public BigInteger Y { get; }

    public ECPoint(BigInteger x, BigInteger y)
    {
        X = x;
        Y = y;
    }

    public bool Equals(ECPoint? other)
    {
        if (other is null) return false;
        return X == other.X && Y == other.Y;
    }

    public override bool Equals(object? obj) => Equals(obj as ECPoint);

    public override int GetHashCode() => HashCode.Combine(X, Y);

    public static bool operator ==(ECPoint? left, ECPoint? right)
    {
        if (left is null) return right is null;
        return left.Equals(right);
    }

    public static bool operator !=(ECPoint? left, ECPoint? right) => !(left == right);

    /// <summary>
    /// Converts the point to a 64-byte array (32 bytes X + 32 bytes Y).
    /// </summary>
    public byte[] ToBytes()
    {
        var xBytes = X.ToByteArray(isUnsigned: true, isBigEndian: true);
        var yBytes = Y.ToByteArray(isUnsigned: true, isBigEndian: true);

        // Pad to 32 bytes each
        var result = new byte[64];
        Array.Copy(xBytes, 0, result, 32 - xBytes.Length, xBytes.Length);
        Array.Copy(yBytes, 0, result, 64 - yBytes.Length, yBytes.Length);

        return result;
    }

    /// <summary>
    /// Creates an ECPoint from a 64-byte array (32 bytes X + 32 bytes Y).
    /// </summary>
    public static ECPoint FromBytes(byte[] bytes)
    {
        if (bytes.Length != 64)
            throw new ArgumentException("Expected 64 bytes", nameof(bytes));

        var xBytes = new byte[32];
        var yBytes = new byte[32];
        Array.Copy(bytes, 0, xBytes, 0, 32);
        Array.Copy(bytes, 32, yBytes, 0, 32);

        var x = new BigInteger(xBytes, isUnsigned: true, isBigEndian: true);
        var y = new BigInteger(yBytes, isUnsigned: true, isBigEndian: true);

        return new ECPoint(x, y);
    }

    /// <summary>
    /// Creates an ECPoint from separate X and Y byte arrays.
    /// </summary>
    public static ECPoint FromCoordinates(byte[] xBytes, byte[] yBytes)
    {
        var x = new BigInteger(xBytes, isUnsigned: true, isBigEndian: true);
        var y = new BigInteger(yBytes, isUnsigned: true, isBigEndian: true);
        return new ECPoint(x, y);
    }

    public override string ToString() => $"({X}, {Y})";
}
