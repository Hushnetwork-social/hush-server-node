using System.Security.Cryptography;
using System.Text;
using Olimpo.KeyDerivation;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;

namespace HushIdentityCompatibilityConformance.Adapters;

/// <summary>
/// Producer derivation adapters (contracts C-A and C-B) wrapping the historical
/// Olimpo.KeyDerivation behavior without changing producer defaults.
///
///   P-01 (Hush Feeds Web Client, TypeScript): BIP-39 seed; HKDF-SHA256 info
///       "signing"/"encryption"; 32-byte output used directly as the scalar;
///       compressed public keys; 12 or 24 words; no scalar retry.
///   P-02 (Olimpo.KeyDerivation, .NET): 24 words; HKDF info
///       "hush/signing/secp256k1/v1"/"hush/encrypt/secp256k1/v1"; invalid-scalar
///       retry "{info}/{attempt}"; uncompressed public keys.
///   P-03 (Hush Desktop): wraps the P-02 path; candidates deduplicate.
/// </summary>
public static class DerivationAdapters
{
    public const string P01 = "P-01";
    public const string P02 = "P-02";
    public const string P03 = "P-03";

    public sealed record DerivedKeys(string SigningPrivateKey, string EncryptionPrivateKey, string SigningAddress, string EncryptionAddress, string PublicKeyEncoding);

    private static readonly X9ECParameters Curve = ECNamedCurveTable.GetByName("secp256k1");
    private static readonly Org.BouncyCastle.Math.BigInteger CurveOrder = Curve.N;

    /// <summary>Deterministic typed failure codes (mirror the TypeScript API).</summary>
    public static string NormalizeMnemonicOlimpo(string mnemonic)
    {
        return string.Join(' ', mnemonic.ToLowerInvariant().Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries));
    }

    public static int CountWords(string mnemonic)
    {
        if (string.IsNullOrWhiteSpace(mnemonic)) return 0;
        return mnemonic.Trim().Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>Generic BIP-39 word/checksum validation; wordCountPolicy 0 = any supported count.</summary>
    public static (bool Valid, string? Code) ValidateMnemonicForProducer(string mnemonic, string producerId)
    {
        if (producerId == P01)
        {
            if (mnemonic.Trim().Length == 0) return (false, "INVALID_MNEMONIC");
            var words = mnemonic.Trim().Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != 12 && words.Length != 24) return (false, "INVALID_WORD_COUNT");
            if (words.Any(w => w != w.ToLowerInvariant())) return (false, "INVALID_MNEMONIC");
            if (words.Any(w => Bip39Wordlist.GetIndex(w) < 0)) return (false, "UNKNOWN_WORD");
            if (!ChecksumMatches(words)) return (false, "INVALID_CHECKSUM");
            return (true, null);
        }

        if (producerId == P02 || producerId == P03)
        {
            if (mnemonic.Trim().Length == 0) return (false, "INVALID_MNEMONIC");
            var normalized = NormalizeMnemonicOlimpo(mnemonic);
            var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != 24) return (false, "INVALID_WORD_COUNT");
            if (words.Any(w => Bip39Wordlist.GetIndex(w) < 0)) return (false, "UNKNOWN_WORD");
            if (!ChecksumMatches(words)) return (false, "INVALID_CHECKSUM");
            return (true, null);
        }

        return (false, "UNSUPPORTED_PRODUCER");
    }

    private static bool ChecksumMatches(string[] words)
    {
        const int bitsPerWord = 11;
        var bits = new System.Text.StringBuilder(words.Length * bitsPerWord);
        foreach (var w in words)
        {
            var idx = Bip39Wordlist.GetIndex(w);
            if (idx < 0) return false;
            bits.Append(Convert.ToString(idx, 2).PadLeft(bitsPerWord, '0'));
        }
        var totalBits = words.Length * bitsPerWord;
        var checksumBits = totalBits % 32;
        var entropyBits = totalBits - checksumBits;
        var entropyBytes = new byte[entropyBits / 8];
        for (var i = 0; i < entropyBytes.Length; i++)
        {
            entropyBytes[i] = Convert.ToByte(bits.ToString(i * 8, 8), 2);
        }
        var hash = SHA256.HashData(entropyBytes);
        var expectedChecksum = Convert.ToString(hash[0], 2).PadLeft(8, '0')[..checksumBits];
        return bits.ToString(entropyBits, checksumBits) == expectedChecksum;
    }

    /// <summary>BIP-39 seed: PBKDF2-HMAC-SHA512, 2048 iterations, salt "mnemonic".</summary>
    public static byte[] MnemonicToSeed(string mnemonic)
    {
        return MnemonicGenerator.MnemonicToSeed(mnemonic);
    }

    /// <summary>HKDF-SHA256 with null salt (RFC 5869 default of 32 zero bytes).</summary>
    public static byte[] HkdfSha256(byte[] ikm, string info, int length = 32)
    {
        var infoBytes = Encoding.UTF8.GetBytes(info);
        var generator = new HkdfBytesGenerator(new Sha256Digest());
        generator.Init(new HkdfParameters(ikm, null, infoBytes));
        var output = new byte[length];
        generator.GenerateBytes(output, 0, length);
        return output;
    }

    public static bool IsUsableScalar(string privateKeyHex)
    {
        try
        {
            var value = new Org.BouncyCastle.Math.BigInteger(privateKeyHex, 16);
            return value.SignValue > 0 && value.CompareTo(CurveOrder) < 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Derive a secp256k1 public key from a private scalar, compressed or uncompressed.</summary>
    public static string DerivePublicKey(string privateKeyHex, bool compressed)
    {
        if (!IsUsableScalar(privateKeyHex)) throw new ArgumentException("invalid private scalar");
        var d = new Org.BouncyCastle.Math.BigInteger(privateKeyHex, 16);
        var point = Curve.G.Multiply(d).Normalize();
        return Convert.ToHexString(point.GetEncoded(compressed)).ToLowerInvariant();
    }

    /// <summary>Decode a compressed/uncompressed public key to its point coordinates.</summary>
    public static (string XHex, string YHex)? DecodePublicKeyPoint(string publicKeyHex)
    {
        try
        {
            var point = Curve.Curve.DecodePoint(HexToBytes(publicKeyHex));
            return (point.XCoord.ToBigInteger().ToString(16).PadLeft(64, '0'),
                    point.YCoord.ToBigInteger().ToString(16).PadLeft(64, '0'));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Contract C-A (P-01): no scalar retry; compressed keys.</summary>
    public static (bool Ok, string? Code, DerivedKeys? Keys) DeriveP01Keys(string mnemonic)
    {
        var seed = MnemonicToSeed(mnemonic);
        var signing = HkdfSha256(seed, "signing");
        var encryption = HkdfSha256(seed, "encryption");
        var signingHex = Convert.ToHexString(signing).ToLowerInvariant();
        var encryptionHex = Convert.ToHexString(encryption).ToLowerInvariant();
        if (!IsUsableScalar(signingHex) || !IsUsableScalar(encryptionHex))
        {
            return (false, "DERIVATION_FAILURE", null);
        }
        return (true, null, new DerivedKeys(
            signingHex,
            encryptionHex,
            DerivePublicKey(signingHex, compressed: true),
            DerivePublicKey(encryptionHex, compressed: true),
            "COMPRESSED"));
    }

    /// <summary>Contract C-B (P-02): invalid-scalar retry "{info}/{attempt}"; uncompressed keys.</summary>
    public static DerivedKeys DeriveP02Keys(string mnemonic)
    {
        var normalized = NormalizeMnemonicOlimpo(mnemonic);
        var seed = MnemonicToSeed(normalized);
        var signing = DeriveWithRetry(seed, "hush/signing/secp256k1/v1");
        var encryption = DeriveWithRetry(seed, "hush/encrypt/secp256k1/v1");
        return new DerivedKeys(
            signing,
            encryption,
            DerivePublicKey(signing, compressed: false),
            DerivePublicKey(encryption, compressed: false),
            "UNCOMPRESSED");
    }

    private static string DeriveWithRetry(byte[] seed, string info)
    {
        var attempt = 0;
        var keyMaterial = HkdfSha256(seed, info);
        while (!IsUsableScalar(Convert.ToHexString(keyMaterial)))
        {
            attempt += 1;
            keyMaterial = HkdfSha256(seed, $"{info}/{attempt}");
        }
        return Convert.ToHexString(keyMaterial).ToLowerInvariant();
    }

    /// <summary>Approved derivation producers in frozen precedence order.</summary>
    public static readonly (string ProducerId, string Name, int Precedence, string MnemonicSupport, string Encoding)[] ApprovedProducers =
    {
        (P01, "Hush Feeds Web Client (TypeScript)", 1, "12_AND_24", "COMPRESSED"),
        (P02, "Olimpo.KeyDerivation (.NET)", 2, "24", "UNCOMPRESSED"),
        (P03, "Hush Desktop Client (Avalonia, historical HushClient)", 5, "24", "UNCOMPRESSED"),
    };

    public static (bool Ok, string? Code, DerivedKeys? Keys) DeriveProducerKeys(string producerId, string mnemonic)
    {
        if (producerId == P01) return DeriveP01Keys(mnemonic);
        if (producerId == P02 || producerId == P03) return (true, null, DeriveP02Keys(mnemonic));
        return (false, "UNSUPPORTED_PRODUCER", null);
    }

    public static byte[] HexToBytes(string hex)
    {
        return Convert.FromHexString(hex);
    }

    public static string BytesToHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
