using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace HushIdentityCompatibilityConformance.Adapters;

/// <summary>
/// Pure bounded .dat v1 compatibility operations (contract C-C) mirroring the
/// historical CredentialsFileService envelope (HUSH magic, version 1, 16-byte
/// salt, 12-byte nonce, PBKDF2-HMAC-SHA256 100k iterations, AES-256-GCM) plus
/// the strict PortableCredentials parser with an exact property allowlist.
/// No file pickers, password prompts, storage, or UI.
/// </summary>
public static class DatAdapter
{
    public const string DatMagic = "HUSH";
    public const int DatVersion = 1;
    public const int DatSaltSize = 16;
    public const int DatNonceSize = 12;
    public const int DatPbkdf2Iterations = 100_000;
    public const long DatMaxEnvelopeBytes = 1024 * 1024; // 1 MiB (corpus maxEnvelopeBytes)
    public const int ProfileNameMaxLength = 64;

    private static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes(DatMagic);

    public sealed record PortableCredentialsRecord(
        string ProfileName,
        string PublicSigningAddress,
        string PrivateSigningKey,
        string PublicEncryptAddress,
        string PrivateEncryptKey,
        bool IsPublic,
        string? Mnemonic);

    public sealed record DatDecodeResult(PortableCredentialsRecord Record, bool MnemonicKeyConsistent, bool PrivatePublicConsistent);

    private static readonly HashSet<string> AllowedFields = new()
    {
        "ProfileName", "PublicSigningAddress", "PrivateSigningKey",
        "PublicEncryptAddress", "PrivateEncryptKey", "IsPublic", "Mnemonic",
    };

    /// <summary>Structural envelope checks (magic, version, minimums, size bound).</summary>
    public static (bool Ok, string? Code, int Version) InspectDatEnvelope(byte[] envelope)
    {
        if (envelope.Length < 36) return (false, "DAT_MALFORMED", 0);
        if (envelope.Length > DatMaxEnvelopeBytes) return (false, "DAT_MALFORMED", 0);
        if (!envelope.Take(4).SequenceEqual(MagicBytes)) return (false, "DAT_INVALID_MAGIC", 0);
        var version = BitConverter.ToInt32(envelope, 4);
        if (version != DatVersion) return (false, "DAT_UNSUPPORTED_VERSION", version);
        return (true, null, version);
    }

    /// <summary>Decrypt a v1 envelope with the exact legacy password-byte behavior.</summary>
    public static (bool Ok, string? Code, string? Plaintext) DecryptDatV1(byte[] envelope, string password)
    {
        var inspection = InspectDatEnvelope(envelope);
        if (!inspection.Ok) return (false, inspection.Code, null);
        var salt = envelope.AsSpan(8, DatSaltSize).ToArray();
        var nonce = envelope.AsSpan(24, DatNonceSize).ToArray();
        var ciphertext = envelope.AsSpan(36).ToArray();
        try
        {
            var key = DeriveKey(password, salt);
            var cipher = new GcmBlockCipher(new AesEngine());
            cipher.Init(false, new AeadParameters(new KeyParameter(key), 128, nonce));
            var plaintext = new byte[cipher.GetOutputSize(ciphertext.Length)];
            var len = cipher.ProcessBytes(ciphertext, 0, ciphertext.Length, plaintext, 0);
            cipher.DoFinal(plaintext, len);
            return (true, null, Encoding.UTF8.GetString(plaintext, 0, plaintext.Length));
        }
        catch (Org.BouncyCastle.Crypto.InvalidCipherTextException)
        {
            return (false, "DAT_WRONG_PASSWORD", null);
        }
        catch
        {
            return (false, "DAT_WRONG_PASSWORD", null);
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var generator = new Pkcs5S2ParametersGenerator(new Sha256Digest());
        generator.Init(passwordBytes, salt, DatPbkdf2Iterations);
        var key = (KeyParameter)generator.GenerateDerivedMacParameters(256);
        return key.GetKey();
    }

    private static readonly Regex KeyPattern = new("\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:", RegexOptions.Compiled);

    private static bool HasDuplicateKeys(string jsonText)
    {
        var keys = KeyPattern.Matches(jsonText).Select(m => m.Groups[1].Value).ToList();
        return keys.Distinct(StringComparer.Ordinal).Count() != keys.Count;
    }

    private static bool IsWellFormedJson(string jsonText)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Strict parse: exact property allowlist; duplicates and unknown fields fail.</summary>
    public static (bool Ok, string? Code, PortableCredentialsRecord? Record) ParsePortableCredentialsStrict(string jsonText)
    {
        if (!IsWellFormedJson(jsonText)) return (false, "DAT_MALFORMED", null);
        if (HasDuplicateKeys(jsonText)) return (false, "DAT_DUPLICATE_FIELD", null);

        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;
        foreach (var prop in root.EnumerateObject())
        {
            if (!AllowedFields.Contains(prop.Name)) return (false, "DAT_UNKNOWN_FIELD", null);
        }
        foreach (var field in AllowedFields)
        {
            if (!root.TryGetProperty(field, out _)) return (false, "DAT_MISSING_FIELD", null);
        }

        var profileName = root.GetProperty("ProfileName").GetString() ?? string.Empty;
        var signingAddress = root.GetProperty("PublicSigningAddress").GetString() ?? string.Empty;
        var signingPrivate = root.GetProperty("PrivateSigningKey").GetString() ?? string.Empty;
        var encryptAddress = root.GetProperty("PublicEncryptAddress").GetString() ?? string.Empty;
        var encryptPrivate = root.GetProperty("PrivateEncryptKey").GetString() ?? string.Empty;
        var mnemonicProp = root.GetProperty("Mnemonic");
        var isPublicProp = root.GetProperty("IsPublic");

        if (profileName.Length == 0 || profileName.Length > ProfileNameMaxLength || profileName.Any(c => c < 0x20 || c == 0x7f))
        {
            return (false, "DAT_INVALID_FIELD", null);
        }
        if (signingAddress.Length == 0 || signingPrivate.Length == 0 || encryptAddress.Length == 0 || encryptPrivate.Length == 0)
        {
            return (false, "DAT_INVALID_FIELD", null);
        }
        if (isPublicProp.ValueKind != JsonValueKind.True && isPublicProp.ValueKind != JsonValueKind.False)
        {
            return (false, "DAT_INVALID_FIELD", null);
        }
        string? mnemonic = null;
        if (mnemonicProp.ValueKind == JsonValueKind.String)
        {
            mnemonic = mnemonicProp.GetString();
        }
        else if (mnemonicProp.ValueKind != JsonValueKind.Null)
        {
            return (false, "DAT_INVALID_FIELD", null);
        }

        return (true, null, new PortableCredentialsRecord(
            profileName, signingAddress, signingPrivate, encryptAddress, encryptPrivate, isPublicProp.GetBoolean(), mnemonic));
    }

    /// <summary>Key consistency: private/public pairs and mnemonic-derived pairs must match.</summary>
    public static (bool PrivatePublicConsistent, bool MnemonicKeyConsistent) ValidateKeyConsistency(PortableCredentialsRecord record)
    {
        var signingPub = DerivePublicFromPrivate(record.PrivateSigningKey, record.PublicSigningAddress.StartsWith("04", StringComparison.Ordinal));
        var encryptPub = DerivePublicFromPrivate(record.PrivateEncryptKey, record.PublicEncryptAddress.StartsWith("04", StringComparison.Ordinal));
        var privatePublicConsistent =
            signingPub is not null && string.Equals(signingPub, record.PublicSigningAddress, StringComparison.OrdinalIgnoreCase) &&
            encryptPub is not null && string.Equals(encryptPub, record.PublicEncryptAddress, StringComparison.OrdinalIgnoreCase);

        var mnemonicKeyConsistent = false;
        if (record.Mnemonic is not null)
        {
            var pairs = new List<(string Signing, string Encryption)>();
            var p01 = DerivationAdapters.DeriveP01Keys(record.Mnemonic);
            if (p01.Ok) pairs.Add((p01.Keys!.SigningAddress, p01.Keys.EncryptionAddress));
            var p02 = DerivationAdapters.DeriveP02Keys(DerivationAdapters.NormalizeMnemonicOlimpo(record.Mnemonic));
            pairs.Add((p02.SigningAddress, p02.EncryptionAddress));
            mnemonicKeyConsistent = pairs.Any(p =>
                string.Equals(p.Signing, record.PublicSigningAddress, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Encryption, record.PublicEncryptAddress, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            mnemonicKeyConsistent = true;
        }

        return (privatePublicConsistent, mnemonicKeyConsistent);
    }

    private static string? DerivePublicFromPrivate(string privateKeyHex, bool uncompressed)
    {
        try
        {
            return DerivationAdapters.DerivePublicKey(privateKeyHex, !uncompressed);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Full pure .dat v1 decode: decrypt -> strict parse -> consistency.</summary>
    public static (bool Ok, string? Code, DatDecodeResult? Result) DecodeDatV1(byte[] envelope, string password)
    {
        var decrypted = DecryptDatV1(envelope, password);
        if (!decrypted.Ok) return (false, decrypted.Code, null);
        var parsed = ParsePortableCredentialsStrict(decrypted.Plaintext!);
        if (!parsed.Ok) return (false, parsed.Code, null);
        var consistency = ValidateKeyConsistency(parsed.Record!);
        if (!consistency.PrivatePublicConsistent) return (false, "DAT_KEY_MISMATCH", null);
        if (!consistency.MnemonicKeyConsistent) return (false, "DAT_MNEMONIC_KEY_MISMATCH", null);
        return (true, null, new DatDecodeResult(parsed.Record!, consistency.MnemonicKeyConsistent, consistency.PrivatePublicConsistent));
    }
}
