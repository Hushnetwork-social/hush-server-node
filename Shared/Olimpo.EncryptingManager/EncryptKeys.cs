using System;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace Olimpo;

/// <summary>
/// Provides ECIES (Elliptic Curve Integrated Encryption Scheme) encryption using secp256k1.
/// Uses ECDH for key agreement + AES-256-GCM for symmetric encryption.
/// </summary>
public class EncryptKeys
{
    private static readonly X9ECParameters Curve = ECNamedCurveTable.GetByName("secp256k1");
    private static readonly ECDomainParameters DomainParams = new ECDomainParameters(
        Curve.Curve, Curve.G, Curve.N, Curve.H, Curve.GetSeed());

    private const int AesKeySize = 32;   // 256 bits
    private const int GcmNonceSize = 12; // 96 bits (recommended for GCM)
    private const int GcmTagSize = 128;  // 128 bits authentication tag

    public string PublicKey { get; private set; }
    public string PrivateKey { get; private set; }

    /// <summary>
    /// Generates a new random secp256k1 key pair for encryption.
    /// </summary>
    public EncryptKeys()
    {
        var random = new SecureRandom();
        var keyBytes = new byte[32];
        random.NextBytes(keyBytes);

        var privateKeyValue = new BigInteger(1, keyBytes);

        // Ensure valid key (0 < key < N)
        while (privateKeyValue.CompareTo(BigInteger.One) < 0 || privateKeyValue.CompareTo(Curve.N) >= 0)
        {
            random.NextBytes(keyBytes);
            privateKeyValue = new BigInteger(1, keyBytes);
        }

        var publicKeyPoint = Curve.G.Multiply(privateKeyValue).Normalize();

        this.PrivateKey = ToHex(privateKeyValue.ToByteArrayUnsigned());
        this.PublicKey = ToHex(publicKeyPoint.GetEncoded(false)); // Uncompressed (65 bytes)
    }

    /// <summary>
    /// Encrypts a message using ECIES (ECDH + AES-256-GCM).
    /// </summary>
    /// <param name="message">Plaintext message to encrypt</param>
    /// <param name="recipientPublicKey">Recipient's public key (hex-encoded, 65 bytes uncompressed)</param>
    /// <returns>Base64-encoded ciphertext: ephemeralPublicKey + nonce + ciphertext + tag</returns>
    public static string Encrypt(string message, string recipientPublicKey)
    {
        // Parse recipient's public key
        var recipientPubKeyBytes = FromHex(recipientPublicKey);
        var recipientPubKeyPoint = Curve.Curve.DecodePoint(recipientPubKeyBytes);
        var recipientPubKeyParams = new ECPublicKeyParameters("EC", recipientPubKeyPoint, DomainParams);

        // Generate ephemeral key pair
        var random = new SecureRandom();
        var ephemeralPrivateBytes = new byte[32];
        random.NextBytes(ephemeralPrivateBytes);
        var ephemeralPrivateValue = new BigInteger(1, ephemeralPrivateBytes);

        // Ensure valid ephemeral key
        while (ephemeralPrivateValue.CompareTo(BigInteger.One) < 0 || ephemeralPrivateValue.CompareTo(Curve.N) >= 0)
        {
            random.NextBytes(ephemeralPrivateBytes);
            ephemeralPrivateValue = new BigInteger(1, ephemeralPrivateBytes);
        }

        var ephemeralPublicPoint = Curve.G.Multiply(ephemeralPrivateValue).Normalize();
        var ephemeralPrivateParams = new ECPrivateKeyParameters("EC", ephemeralPrivateValue, DomainParams);

        // Perform ECDH key agreement
        var agreement = new ECDHBasicAgreement();
        agreement.Init(ephemeralPrivateParams);
        var sharedSecret = agreement.CalculateAgreement(recipientPubKeyParams);
        var sharedSecretBytes = sharedSecret.ToByteArrayUnsigned();

        // Derive AES key from shared secret using HKDF
        var aesKey = DeriveAesKey(sharedSecretBytes, ephemeralPublicPoint.GetEncoded(false));

        // Encrypt message with AES-256-GCM
        var plaintextBytes = Encoding.UTF8.GetBytes(message);
        var nonce = new byte[GcmNonceSize];
        random.NextBytes(nonce);

        var cipher = new GcmBlockCipher(new AesEngine());
        var parameters = new AeadParameters(new KeyParameter(aesKey), GcmTagSize, nonce);
        cipher.Init(true, parameters);

        var ciphertext = new byte[cipher.GetOutputSize(plaintextBytes.Length)];
        var len = cipher.ProcessBytes(plaintextBytes, 0, plaintextBytes.Length, ciphertext, 0);
        cipher.DoFinal(ciphertext, len);

        // Combine: ephemeralPublicKey (65 bytes) + nonce (12 bytes) + ciphertext (includes tag)
        var ephemeralPubKeyBytes = ephemeralPublicPoint.GetEncoded(false);
        var result = new byte[ephemeralPubKeyBytes.Length + nonce.Length + ciphertext.Length];
        Buffer.BlockCopy(ephemeralPubKeyBytes, 0, result, 0, ephemeralPubKeyBytes.Length);
        Buffer.BlockCopy(nonce, 0, result, ephemeralPubKeyBytes.Length, nonce.Length);
        Buffer.BlockCopy(ciphertext, 0, result, ephemeralPubKeyBytes.Length + nonce.Length, ciphertext.Length);

        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// Decrypts a message using ECIES (ECDH + AES-256-GCM).
    /// </summary>
    /// <param name="encryptedMessage">Base64-encoded ciphertext from Encrypt()</param>
    /// <param name="privateKey">Recipient's private key (hex-encoded, 32 bytes)</param>
    /// <returns>Decrypted plaintext message</returns>
    public static string Decrypt(string encryptedMessage, string privateKey)
    {
        var encryptedBytes = Convert.FromBase64String(encryptedMessage);

        // Extract ephemeral public key (65 bytes), nonce (12 bytes), and ciphertext
        const int ephemeralPubKeyLen = 65;
        var ephemeralPubKeyBytes = new byte[ephemeralPubKeyLen];
        var nonce = new byte[GcmNonceSize];
        var ciphertext = new byte[encryptedBytes.Length - ephemeralPubKeyLen - GcmNonceSize];

        Buffer.BlockCopy(encryptedBytes, 0, ephemeralPubKeyBytes, 0, ephemeralPubKeyLen);
        Buffer.BlockCopy(encryptedBytes, ephemeralPubKeyLen, nonce, 0, GcmNonceSize);
        Buffer.BlockCopy(encryptedBytes, ephemeralPubKeyLen + GcmNonceSize, ciphertext, 0, ciphertext.Length);

        // Parse ephemeral public key
        var ephemeralPubKeyPoint = Curve.Curve.DecodePoint(ephemeralPubKeyBytes);
        var ephemeralPubKeyParams = new ECPublicKeyParameters("EC", ephemeralPubKeyPoint, DomainParams);

        // Parse recipient's private key
        var privateKeyBytes = FromHex(privateKey);
        var privateKeyValue = new BigInteger(1, privateKeyBytes);
        var privateKeyParams = new ECPrivateKeyParameters("EC", privateKeyValue, DomainParams);

        // Perform ECDH key agreement
        var agreement = new ECDHBasicAgreement();
        agreement.Init(privateKeyParams);
        var sharedSecret = agreement.CalculateAgreement(ephemeralPubKeyParams);
        var sharedSecretBytes = sharedSecret.ToByteArrayUnsigned();

        // Derive AES key from shared secret using HKDF
        var aesKey = DeriveAesKey(sharedSecretBytes, ephemeralPubKeyBytes);

        // Decrypt with AES-256-GCM
        var cipher = new GcmBlockCipher(new AesEngine());
        var parameters = new AeadParameters(new KeyParameter(aesKey), GcmTagSize, nonce);
        cipher.Init(false, parameters);

        var plaintext = new byte[cipher.GetOutputSize(ciphertext.Length)];
        var len = cipher.ProcessBytes(ciphertext, 0, ciphertext.Length, plaintext, 0);
        cipher.DoFinal(plaintext, len);

        return Encoding.UTF8.GetString(plaintext);
    }

    /// <summary>
    /// Derives an AES-256 key from the ECDH shared secret using HKDF.
    /// </summary>
    private static byte[] DeriveAesKey(byte[] sharedSecret, byte[] ephemeralPublicKey)
    {
        var generator = new HkdfBytesGenerator(new Sha256Digest());
        // Use ephemeral public key as salt, "hush/ecies/aes256gcm/v1" as info
        var info = Encoding.UTF8.GetBytes("hush/ecies/aes256gcm/v1");
        generator.Init(new HkdfParameters(sharedSecret, ephemeralPublicKey, info));

        var aesKey = new byte[AesKeySize];
        generator.GenerateBytes(aesKey, 0, AesKeySize);
        return aesKey;
    }

    #region AES-256-GCM Standalone Methods (for symmetric encryption)

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

    #region Hex Utilities

    private static string ToHex(byte[] data) => string.Concat(data.Select(x => x.ToString("x2")));

    private static byte[] FromHex(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return bytes;
    }

    #endregion
}
