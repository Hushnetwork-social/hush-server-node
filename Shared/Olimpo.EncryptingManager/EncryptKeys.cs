using System;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
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
        // Check if it's the new pipe-separated format or old XML format
        if (serialized.Contains('|'))
        {
            // New format: Modulus|Exponent (both Base64 encoded)
            var parts = serialized.Split('|');
            var modulus = new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(parts[0]));
            var exponent = new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(parts[1]));
            return new RsaKeyParameters(false, modulus, exponent);
        }
        else
        {
            // Old format: Base64-encoded XML RSAKeyValue
            return ParseXmlPublicKey(serialized);
        }
    }

    private static RsaKeyParameters ParseXmlPublicKey(string base64Xml)
    {
        var xml = Encoding.UTF8.GetString(Convert.FromBase64String(base64Xml));

        // Parse XML manually to avoid System.Security.Cryptography dependencies
        var modulusStart = xml.IndexOf("<Modulus>") + 9;
        var modulusEnd = xml.IndexOf("</Modulus>");
        var exponentStart = xml.IndexOf("<Exponent>") + 10;
        var exponentEnd = xml.IndexOf("</Exponent>");

        var modulusBase64 = xml.Substring(modulusStart, modulusEnd - modulusStart);
        var exponentBase64 = xml.Substring(exponentStart, exponentEnd - exponentStart);

        var modulus = new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(modulusBase64));
        var exponent = new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(exponentBase64));

        return new RsaKeyParameters(false, modulus, exponent);
    }

    private static RsaPrivateCrtKeyParameters DeserializePrivateKey(string serialized)
    {
        // Check if it's the new pipe-separated format or old XML format
        if (serialized.Contains('|'))
        {
            // New format: pipe-separated Base64 values
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
        else
        {
            // Old format: Base64-encoded XML RSAKeyValue
            return ParseXmlPrivateKey(serialized);
        }
    }

    private static RsaPrivateCrtKeyParameters ParseXmlPrivateKey(string base64Xml)
    {
        var xml = Encoding.UTF8.GetString(Convert.FromBase64String(base64Xml));

        // Parse XML manually to extract RSA private key components
        var modulus = ParseXmlElement(xml, "Modulus");
        var exponent = ParseXmlElement(xml, "Exponent");
        var d = ParseXmlElement(xml, "D");
        var p = ParseXmlElement(xml, "P");
        var q = ParseXmlElement(xml, "Q");
        var dp = ParseXmlElement(xml, "DP");
        var dq = ParseXmlElement(xml, "DQ");
        var inverseQ = ParseXmlElement(xml, "InverseQ");

        return new RsaPrivateCrtKeyParameters(
            new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(modulus)),
            new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(exponent)),
            new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(d)),
            new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(p)),
            new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(q)),
            new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(dp)),
            new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(dq)),
            new Org.BouncyCastle.Math.BigInteger(1, Convert.FromBase64String(inverseQ)));
    }

    private static string ParseXmlElement(string xml, string elementName)
    {
        var startTag = $"<{elementName}>";
        var endTag = $"</{elementName}>";
        var startIndex = xml.IndexOf(startTag) + startTag.Length;
        var endIndex = xml.IndexOf(endTag);
        return xml.Substring(startIndex, endIndex - startIndex);
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

    #region AES-256-GCM Encryption

    private const int AesKeySize = 32;  // 256 bits
    private const int GcmNonceSize = 12; // 96 bits (recommended for GCM)
    private const int GcmTagSize = 128;  // 128 bits authentication tag

    /// <summary>
    /// Generates a cryptographically secure random AES-256 key.
    /// </summary>
    /// <returns>Base64-encoded AES key (32 bytes)</returns>
    public static string GenerateAesKey()
    {
        var random = new SecureRandom();
        var key = new byte[AesKeySize];
        random.NextBytes(key);
        return Convert.ToBase64String(key);
    }

    /// <summary>
    /// Encrypts plaintext using AES-256-GCM authenticated encryption.
    /// </summary>
    /// <param name="plaintext">The text to encrypt</param>
    /// <param name="aesKeyBase64">Base64-encoded AES-256 key</param>
    /// <returns>Base64-encoded ciphertext (nonce + ciphertext + tag)</returns>
    public static string AesEncrypt(string plaintext, string aesKeyBase64)
    {
        var key = Convert.FromBase64String(aesKeyBase64);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

        // Generate random nonce
        var random = new SecureRandom();
        var nonce = new byte[GcmNonceSize];
        random.NextBytes(nonce);

        // Setup AES-GCM cipher
        var cipher = new GcmBlockCipher(new AesEngine());
        var parameters = new AeadParameters(new KeyParameter(key), GcmTagSize, nonce);
        cipher.Init(true, parameters);

        // Encrypt
        var ciphertext = new byte[cipher.GetOutputSize(plaintextBytes.Length)];
        var len = cipher.ProcessBytes(plaintextBytes, 0, plaintextBytes.Length, ciphertext, 0);
        cipher.DoFinal(ciphertext, len);

        // Combine nonce + ciphertext (tag is appended by GCM)
        var result = new byte[nonce.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
        Buffer.BlockCopy(ciphertext, 0, result, nonce.Length, ciphertext.Length);

        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// Decrypts ciphertext using AES-256-GCM authenticated encryption.
    /// </summary>
    /// <param name="encryptedBase64">Base64-encoded ciphertext (nonce + ciphertext + tag)</param>
    /// <param name="aesKeyBase64">Base64-encoded AES-256 key</param>
    /// <returns>Decrypted plaintext</returns>
    public static string AesDecrypt(string encryptedBase64, string aesKeyBase64)
    {
        var key = Convert.FromBase64String(aesKeyBase64);
        var encryptedBytes = Convert.FromBase64String(encryptedBase64);

        // Extract nonce and ciphertext
        var nonce = new byte[GcmNonceSize];
        var ciphertext = new byte[encryptedBytes.Length - GcmNonceSize];
        Buffer.BlockCopy(encryptedBytes, 0, nonce, 0, GcmNonceSize);
        Buffer.BlockCopy(encryptedBytes, GcmNonceSize, ciphertext, 0, ciphertext.Length);

        // Setup AES-GCM cipher for decryption
        var cipher = new GcmBlockCipher(new AesEngine());
        var parameters = new AeadParameters(new KeyParameter(key), GcmTagSize, nonce);
        cipher.Init(false, parameters);

        // Decrypt
        var plaintext = new byte[cipher.GetOutputSize(ciphertext.Length)];
        var len = cipher.ProcessBytes(ciphertext, 0, ciphertext.Length, plaintext, 0);
        cipher.DoFinal(plaintext, len);

        return Encoding.UTF8.GetString(plaintext);
    }

    #endregion
}
