using System.Linq;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;

namespace Olimpo.KeyDerivation;

/// <summary>
/// Contains all derived keys from a mnemonic.
/// </summary>
public class DerivedKeys
{
    public string SigningPublicKey { get; }
    public string SigningPrivateKey { get; }
    public string EncryptPublicKey { get; }
    public string EncryptPrivateKey { get; }

    public DerivedKeys(
        string signingPublicKey,
        string signingPrivateKey,
        string encryptPublicKey,
        string encryptPrivateKey)
    {
        SigningPublicKey = signingPublicKey;
        SigningPrivateKey = signingPrivateKey;
        EncryptPublicKey = encryptPublicKey;
        EncryptPrivateKey = encryptPrivateKey;
    }
}

/// <summary>
/// Generates deterministic ECDSA keys from a BIP-39 mnemonic.
/// Both signing and encryption use secp256k1 for fast key derivation.
/// </summary>
public static class DeterministicKeyGenerator
{
    private const string SigningKeyInfo = "hush/signing/secp256k1/v1";
    private const string EncryptKeyInfo = "hush/encrypt/secp256k1/v1";

    private const int EcKeyLength = 32;             // 256 bits for secp256k1

    /// <summary>
    /// Derives all keys from a BIP-39 mnemonic.
    /// </summary>
    /// <param name="mnemonic">24-word BIP-39 mnemonic</param>
    /// <param name="passphrase">Optional passphrase (default: empty)</param>
    /// <returns>All derived keys</returns>
    public static DerivedKeys DeriveKeys(string mnemonic, string passphrase = "")
    {
        if (!MnemonicGenerator.ValidateMnemonic(mnemonic))
            throw new ArgumentException("Invalid mnemonic", nameof(mnemonic));

        var masterSeed = MnemonicGenerator.MnemonicToSeed(mnemonic, passphrase);

        var (signingPublic, signingPrivate) = DeriveSigningKeys(masterSeed);
        var (encryptPublic, encryptPrivate) = DeriveEncryptKeys(masterSeed);

        return new DerivedKeys(
            signingPublicKey: signingPublic,
            signingPrivateKey: signingPrivate,
            encryptPublicKey: encryptPublic,
            encryptPrivateKey: encryptPrivate
        );
    }

    /// <summary>
    /// Derives ECDSA secp256k1 signing keys from a master seed.
    /// Uses HKDF to derive key material, then uses it as the private key scalar.
    /// </summary>
    /// <param name="masterSeed">64-byte master seed from BIP-39</param>
    /// <returns>Tuple of (publicKey, privateKey) as hex strings</returns>
    public static (string publicKey, string privateKey) DeriveSigningKeys(byte[] masterSeed)
    {
        return DeriveEcKeys(masterSeed, SigningKeyInfo);
    }

    /// <summary>
    /// Derives secp256k1 encryption keys from a master seed.
    /// Uses HKDF to derive key material (same approach as signing keys, different info string).
    /// </summary>
    /// <param name="masterSeed">64-byte master seed from BIP-39</param>
    /// <returns>Tuple of (publicKey, privateKey) as hex strings</returns>
    public static (string publicKey, string privateKey) DeriveEncryptKeys(byte[] masterSeed)
    {
        return DeriveEcKeys(masterSeed, EncryptKeyInfo);
    }

    /// <summary>
    /// Derives secp256k1 EC keys from a master seed using HKDF.
    /// Shared implementation for both signing and encryption keys.
    /// </summary>
    /// <param name="masterSeed">64-byte master seed from BIP-39</param>
    /// <param name="infoString">HKDF info string to differentiate key types</param>
    /// <returns>Tuple of (publicKey, privateKey) as hex strings</returns>
    private static (string publicKey, string privateKey) DeriveEcKeys(byte[] masterSeed, string infoString)
    {
        // Use HKDF to derive 32 bytes of key material
        var keyMaterial = Hkdf(masterSeed, infoString, EcKeyLength);

        // Get curve parameters
        var curve = ECNamedCurveTable.GetByName("secp256k1");
        var domainParams = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H, curve.GetSeed());

        // Convert to BigInteger and ensure it's valid (0 < key < N)
        var privateKeyValue = new BigInteger(1, keyMaterial);

        // If key >= N or key == 0, derive again with incremented info (extremely rare)
        int attempt = 0;
        while (privateKeyValue.CompareTo(BigInteger.One) < 0 || privateKeyValue.CompareTo(curve.N) >= 0)
        {
            attempt++;
            keyMaterial = Hkdf(masterSeed, $"{infoString}/{attempt}", EcKeyLength);
            privateKeyValue = new BigInteger(1, keyMaterial);
        }

        // Create key parameters
        var privateKeyParams = new ECPrivateKeyParameters("EC", privateKeyValue, domainParams);

        // Derive public key: Q = d * G
        var publicKeyPoint = curve.G.Multiply(privateKeyValue).Normalize();

        // Convert to hex strings
        var privateKeyHex = ToHex(privateKeyParams.D.ToByteArrayUnsigned());
        var publicKeyHex = ToHex(publicKeyPoint.GetEncoded(false)); // Uncompressed point (65 bytes)

        return (publicKeyHex, privateKeyHex);
    }

    /// <summary>
    /// HKDF (HMAC-based Key Derivation Function) using SHA-256.
    /// </summary>
    private static byte[] Hkdf(byte[] inputKeyMaterial, string info, int outputLength)
    {
        var infoBytes = System.Text.Encoding.UTF8.GetBytes(info);

        var generator = new HkdfBytesGenerator(new Sha256Digest());
        generator.Init(new HkdfParameters(inputKeyMaterial, null, infoBytes));

        var output = new byte[outputLength];
        generator.GenerateBytes(output, 0, outputLength);

        return output;
    }

    /// <summary>
    /// Converts bytes to lowercase hex string.
    /// </summary>
    private static string ToHex(byte[] data) => string.Concat(data.Select(x => x.ToString("x2")));
}
