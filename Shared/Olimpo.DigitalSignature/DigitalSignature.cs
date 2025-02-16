using System;
using System.Linq;
using System.Text;
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
        (this.PrivateKey, this.PublicAddress) = this.GenerateKeyPair();
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

    public static bool VerifySignature(string message, string signature, string publicKeyString)
    {
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        AsymmetricKeyParameter publicKey = GetPublicKeyFromHex(publicKeyString);
        ISigner signer = SignerUtilities.GetSigner("SHA-256withECDSA");
        signer.Init(false, publicKey);
        signer.BlockUpdate(messageBytes, 0, messageBytes.Length);
        return signer.VerifySignature(HexStringToByteArray(signature));
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

    static string ToHex(byte[] data) => String.Concat(data.Select(x => x.ToString("x2")));
}
