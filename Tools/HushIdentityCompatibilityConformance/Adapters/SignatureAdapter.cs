using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;

namespace HushIdentityCompatibilityConformance.Adapters;

/// <summary>
/// Signature compatibility (contract P-07 / Olimpo.DigitalSignature behavior).
/// ECDSA over the SHA-256 prehash of the exact UTF-8 canonical transaction
/// bytes; supports compact 64-byte r||s and DER encodings with lossless
/// conversion. The verify path reproduces BouncyCastle "SHA-256withECDSA"
/// (ECDSA over SHA-256(message)) — the same contract the historical
/// Olimpo.DigitalSignature exposes — using BouncyCastle.Cryptography 2.5.1 so
/// the runner compiles against a single BouncyCastle version.
/// </summary>
public static class SignatureAdapter
{
    private static readonly X9ECParameters Curve = ECNamedCurveTable.GetByName("secp256k1");
    private static readonly ECDomainParameters DomainParams = new(Curve.Curve, Curve.G, Curve.N, Curve.H, Curve.GetSeed());

    /// <summary>DER -> compact 64-byte r||s. Throws when the structure is malformed.</summary>
    public static byte[] DerToCompact(byte[] der)
    {
        if (der.Length < 8 || der[0] != 0x30) throw new InvalidOperationException("not a DER sequence");
        var seqLen = der[1];
        if (seqLen != der.Length - 2) throw new InvalidOperationException("DER sequence length mismatch");
        if (der[2] != 0x02) throw new InvalidOperationException("missing INTEGER tag for r");
        var rLen = der[3];
        if (rLen < 1 || rLen > 33) throw new InvalidOperationException("invalid r length");
        var sTagIndex = 4 + rLen;
        if (sTagIndex >= der.Length || der[sTagIndex] != 0x02) throw new InvalidOperationException("missing INTEGER tag for s");
        var sLen = der[sTagIndex + 1];
        if (sLen < 1 || sLen > 33) throw new InvalidOperationException("invalid s length");
        if (sTagIndex + 2 + sLen != der.Length) throw new InvalidOperationException("trailing bytes after s");
        var r = PadTo32(der.AsSpan(4, rLen).ToArray());
        var s = PadTo32(der.AsSpan(sTagIndex + 2, sLen).ToArray());
        var result = new byte[64];
        Array.Copy(r, 0, result, 0, 32);
        Array.Copy(s, 0, result, 32, 32);
        return result;
    }

    /// <summary>Compact 64-byte r||s -> DER SEQUENCE { INTEGER r, INTEGER s }.</summary>
    public static byte[] CompactToDer(byte[] compact)
    {
        if (compact.Length != 64) throw new InvalidOperationException("compact signature must be 64 bytes");
        var r = EncodeInt(new BigInteger(1, compact.AsSpan(0, 32).ToArray()));
        var s = EncodeInt(new BigInteger(1, compact.AsSpan(32, 32).ToArray()));
        var body = new byte[2 + r.Length + 2 + s.Length];
        body[0] = 0x02;
        body[1] = (byte)r.Length;
        Array.Copy(r, 0, body, 2, r.Length);
        body[2 + r.Length] = 0x02;
        body[3 + r.Length] = (byte)s.Length;
        Array.Copy(s, 0, body, 4 + r.Length, s.Length);
        var result = new byte[2 + body.Length];
        result[0] = 0x30;
        result[1] = (byte)body.Length;
        Array.Copy(body, 0, result, 2, body.Length);
        return result;
    }

    private static byte[] EncodeInt(BigInteger n)
    {
        var bytes = n.ToByteArrayUnsigned();
        if ((bytes[0] & 0x80) != 0)
        {
            var prefixed = new byte[bytes.Length + 1];
            Array.Copy(bytes, 0, prefixed, 1, bytes.Length);
            return prefixed;
        }
        return bytes;
    }

    private static byte[] PadTo32(byte[] bytes)
    {
        if (bytes.Length == 32) return bytes;
        if (bytes.Length == 33 && bytes[0] == 0x00) return bytes.AsSpan(1).ToArray();
        throw new InvalidOperationException("unexpected integer length");
    }

    /// <summary>Decode a signature to compact hex; typed failure for malformed encodings.</summary>
    public static (bool Ok, string? Code, string? CompactHex) DecodeSignature(string signatureHex, string format)
    {
        try
        {
            if (format == "compact")
            {
                var bytes = Convert.FromHexString(signatureHex);
                if (bytes.Length != 64) return (false, "SIGNATURE_MALFORMED", null);
                return (true, null, Convert.ToHexString(bytes).ToLowerInvariant());
            }
            var compact = DerToCompact(Convert.FromHexString(signatureHex));
            return (true, null, Convert.ToHexString(compact).ToLowerInvariant());
        }
        catch
        {
            return (false, "SIGNATURE_MALFORMED", null);
        }
    }

    /// <summary>Verify a compact or DER signature over a UTF-8 message with a hex public key.</summary>
    public static bool VerifyMessage(string messageUtf8, string signatureHex, string publicKeyHex, string format)
    {
        try
        {
            if (format == "der")
            {
                return VerifyDer(messageUtf8, Convert.FromHexString(signatureHex), publicKeyHex);
            }
            var decoded = DecodeSignature(signatureHex, "compact");
            if (!decoded.Ok) return false;
            var compact = Convert.FromHexString(decoded.CompactHex!);
            return VerifyDer(messageUtf8, CompactToDer(compact), publicKeyHex);
        }
        catch
        {
            return false;
        }
    }

    private static bool VerifyDer(string messageUtf8, byte[] derSignature, string publicKeyHex)
    {
        var compact = DerToCompact(derSignature);
        var r = new BigInteger(1, compact.AsSpan(0, 32).ToArray());
        var s = new BigInteger(1, compact.AsSpan(32, 32).ToArray());
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(messageUtf8));
        var publicKey = new ECPublicKeyParameters("ECDSA", Curve.Curve.DecodePoint(Convert.FromHexString(publicKeyHex)), DomainParams);
        var signer = new ECDsaSigner();
        signer.Init(false, publicKey);
        return signer.VerifySignature(hash, r, s);
    }

    public static string BytesToHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
