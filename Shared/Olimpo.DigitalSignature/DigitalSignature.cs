using System;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace Olimpo;

public class DigitalSignature
{
    public string PublicAddress { get; }

    public string PrivateKey { get; }

    public DigitalSignature()
    {
        (PrivateKey, PublicAddress) = GenerateKeyPair();
    }

    public static string SignMessage(string message, string privateKeyString)
    {
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        AsymmetricKeyParameter privateKey = GetPrivateKeyFromHex(privateKeyString);
        ISigner signer = SignerUtilities.GetSigner("SHA-256withECDSA");
        signer.Init(true, privateKey);
        signer.BlockUpdate(messageBytes, 0, messageBytes.Length);
        return ToHex(signer.GenerateSignature());
    }

    public static string SignMessageCompactBase64(string message, string privateKeyString)
    {
        var derSignature = HexStringToByteArray(SignMessage(message, privateKeyString));
        var compactSignature = ConvertDerSignatureToCompact(derSignature);
        return Convert.ToBase64String(compactSignature);
    }

    public static bool VerifySignature(string message, string signature, string publicKeyString)
    {
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        AsymmetricKeyParameter publicKey = GetPublicKeyFromHex(publicKeyString);
        ISigner signer = SignerUtilities.GetSigner("SHA-256withECDSA");
        signer.Init(false, publicKey);
        signer.BlockUpdate(messageBytes, 0, messageBytes.Length);
        return signer.VerifySignature(HexStringToByteArray(signature));
    }

    public static bool VerifyCompactSignature(string message, byte[] compactSignature, string publicKeyString)
    {
        try
        {
            return VerifySignature(
                message,
                ToHex(ConvertCompactSignatureToDer(compactSignature)),
                publicKeyString);
        }
        catch
        {
            return false;
        }
    }

    public static bool VerifyCompactSignatureBase64(string message, string base64Signature, string publicKeyString)
    {
        if (string.IsNullOrWhiteSpace(base64Signature))
        {
            return false;
        }

        try
        {
            return VerifyCompactSignature(message, Convert.FromBase64String(base64Signature), publicKeyString);
        }
        catch
        {
            return false;
        }
    }

    public static string GetCompressedPublicAddress(string privateKeyString)
    {
        var privateKey = (ECPrivateKeyParameters)GetPrivateKeyFromHex(privateKeyString);
        var curve = ECNamedCurveTable.GetByName("secp256k1");
        var publicKey = curve.G.Multiply(privateKey.D).Normalize();
        return ToHex(publicKey.GetEncoded(true));
    }

    private (string privateKey, string publicKey) GenerateKeyPair()
    {
        var curve = ECNamedCurveTable.GetByName("secp256k1");
        var domainParams = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H, curve.GetSeed());

        var secureRandom = new SecureRandom();
        var keyParams = new ECKeyGenerationParameters(domainParams, secureRandom);

        var generator = new ECKeyPairGenerator("ECDSA");
        generator.Init(keyParams);
        var keyPair = generator.GenerateKeyPair();

        var privateKey = keyPair.Private as ECPrivateKeyParameters;
        var publicKey = keyPair.Public as ECPublicKeyParameters;

        if (privateKey == null || publicKey == null)
        {
            throw new InvalidOperationException("Could not create keys");
        }
        else if (privateKey.D == null || publicKey.Q == null)
        {
            throw new InvalidOperationException("Could not create keys");
        }
        else
        {
            return (ToHex(privateKey.D.ToByteArrayUnsigned()), ToHex(publicKey.Q.GetEncoded()));
        }
    }

    private static AsymmetricKeyParameter GetPrivateKeyFromHex(string privateKeyString)
    {
        byte[] privateKeyBytes = HexStringToByteArray(privateKeyString);
        X9ECParameters curve = ECNamedCurveTable.GetByName("secp256k1");
        ECDomainParameters domainParameters = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H, curve.GetSeed());
        BigInteger privateKeyValue = new BigInteger(1, privateKeyBytes);

        return new ECPrivateKeyParameters("ECDSA", privateKeyValue, domainParameters);
    }

    private static AsymmetricKeyParameter GetPublicKeyFromHex(string publicKeyString)
    {
        byte[] publicKeyBytes = HexStringToByteArray(publicKeyString);
        X9ECParameters curve = ECNamedCurveTable.GetByName("secp256k1");
        ECDomainParameters domainParameters = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H, curve.GetSeed());
        ECPublicKeyParameters publicKey = new ECPublicKeyParameters("ECDSA", curve.Curve.DecodePoint(publicKeyBytes), domainParameters);

        return publicKey;
    }

    private static byte[] HexStringToByteArray(string hex)
    {
        int length = hex.Length;
        byte[] bytes = new byte[length / 2];
        for (int i = 0; i < length; i += 2)
        {
            bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
        }
        return bytes;
    }

    private static byte[] ConvertCompactSignatureToDer(byte[] compactSignature)
    {
        if (compactSignature is null || compactSignature.Length != 64)
        {
            throw new ArgumentException("Compact secp256k1 signature must be 64 bytes.", nameof(compactSignature));
        }

        var rBytes = new byte[32];
        var sBytes = new byte[32];
        Array.Copy(compactSignature, 0, rBytes, 0, 32);
        Array.Copy(compactSignature, 32, sBytes, 0, 32);

        var sequence = new DerSequence(
            new DerInteger(new BigInteger(1, rBytes)),
            new DerInteger(new BigInteger(1, sBytes)));
        return sequence.GetDerEncoded();
    }

    private static byte[] ConvertDerSignatureToCompact(byte[] derSignature)
    {
        if (derSignature is null || derSignature.Length == 0)
        {
            throw new ArgumentException("DER signature is required.", nameof(derSignature));
        }

        if (Asn1Object.FromByteArray(derSignature) is not Asn1Sequence sequence || sequence.Count != 2)
        {
            throw new ArgumentException("DER signature must contain exactly two integers.", nameof(derSignature));
        }

        var r = ((DerInteger)sequence[0]).PositiveValue.ToByteArrayUnsigned();
        var s = ((DerInteger)sequence[1]).PositiveValue.ToByteArrayUnsigned();
        var compactSignature = new byte[64];

        CopyPadded(r, compactSignature, 0);
        CopyPadded(s, compactSignature, 32);

        return compactSignature;
    }

    private static void CopyPadded(byte[] source, byte[] destination, int destinationOffset)
    {
        if (source.Length > 32)
        {
            throw new ArgumentException("Signature integer component exceeds 32 bytes.", nameof(source));
        }

        var padding = 32 - source.Length;
        Array.Copy(source, 0, destination, destinationOffset + padding, source.Length);
    }

    private static string ToHex(byte[] data) => string.Concat(data.Select(x => x.ToString("x2")));
}
