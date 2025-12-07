using System;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace Olimpo;

public class EncryptKeys
{
    public string PublicKey { get; private set; }

    public string PrivateKey { get; private set; }

    public EncryptKeys()
    {
        // Use BouncyCastle which works in WebAssembly (pure managed implementation)
        var keyGen = new RsaKeyPairGenerator();
        keyGen.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
        var keyPair = keyGen.GenerateKeyPair();

        var publicKeyParams = (RsaKeyParameters)keyPair.Public;
        var privateKeyParams = (RsaPrivateCrtKeyParameters)keyPair.Private;

        // Serialize keys to a portable format (Base64-encoded parameters)
        this.PublicKey = SerializePublicKey(publicKeyParams);
        this.PrivateKey = SerializePrivateKey(privateKeyParams);
    }

    private static string SerializePublicKey(RsaKeyParameters publicKey)
    {
        // Format: Modulus|Exponent (both Base64 encoded)
        var modulus = Convert.ToBase64String(publicKey.Modulus.ToByteArrayUnsigned());
        var exponent = Convert.ToBase64String(publicKey.Exponent.ToByteArrayUnsigned());
        return $"{modulus}|{exponent}";
    }

    private static string SerializePrivateKey(RsaPrivateCrtKeyParameters privateKey)
    {
        // Format: Modulus|PublicExponent|PrivateExponent|P|Q|DP|DQ|QInv (all Base64 encoded)
        var parts = new[]
        {
            Convert.ToBase64String(privateKey.Modulus.ToByteArrayUnsigned()),
            Convert.ToBase64String(privateKey.PublicExponent.ToByteArrayUnsigned()),
            Convert.ToBase64String(privateKey.Exponent.ToByteArrayUnsigned()),
            Convert.ToBase64String(privateKey.P.ToByteArrayUnsigned()),
            Convert.ToBase64String(privateKey.Q.ToByteArrayUnsigned()),
            Convert.ToBase64String(privateKey.DP.ToByteArrayUnsigned()),
            Convert.ToBase64String(privateKey.DQ.ToByteArrayUnsigned()),
            Convert.ToBase64String(privateKey.QInv.ToByteArrayUnsigned())
        };
        return string.Join("|", parts);
    }

    private static RsaKeyParameters DeserializePublicKey(string serialized)
    {
        var parts = serialized.Split('|');
        var modulus = new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(parts[0]));
        var exponent = new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(parts[1]));
        return new RsaKeyParameters(false, modulus, exponent);
    }

    private static RsaPrivateCrtKeyParameters DeserializePrivateKey(string serialized)
    {
        var parts = serialized.Split('|');
        var modulus = new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(parts[0]));
        var publicExponent = new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(parts[1]));
        var privateExponent = new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(parts[2]));
        var p = new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(parts[3]));
        var q = new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(parts[4]));
        var dp = new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(parts[5]));
        var dq = new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(parts[6]));
        var qInv = new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(parts[7]));

        return new RsaPrivateCrtKeyParameters(modulus, publicExponent, privateExponent, p, q, dp, dq, qInv);
    }

    public static string Encrypt(string message, string publicKey)
    {
        var keyParams = DeserializePublicKey(publicKey);

        var engine = new OaepEncoding(new RsaEngine());
        engine.Init(true, keyParams);

        byte[] dataToEncrypt = Encoding.UTF8.GetBytes(message);
        byte[] encryptedData = engine.ProcessBlock(dataToEncrypt, 0, dataToEncrypt.Length);

        return Convert.ToBase64String(encryptedData);
    }

    public static string Decrypt(string encryptedMessage, string privateKey)
    {
        var keyParams = DeserializePrivateKey(privateKey);

        var engine = new OaepEncoding(new RsaEngine());
        engine.Init(false, keyParams);

        byte[] encryptedData = Convert.FromBase64String(encryptedMessage);
        byte[] decryptedData = engine.ProcessBlock(encryptedData, 0, encryptedData.Length);

        return Encoding.UTF8.GetString(decryptedData);
    }
}
