using System;
using System.Text;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace Olimpo.KeyDerivation;

/// <summary>
/// Generates and validates BIP-39 mnemonics, and converts them to seeds.
/// </summary>
public static class MnemonicGenerator
{
    private const int EntropyBits = 256;        // 256 bits for 24 words
    private const int EntropyBytes = 32;        // 256 / 8
    private const int ChecksumBits = 8;         // 256 / 32 = 8 bits checksum
    private const int WordCount = 24;           // (256 + 8) / 11 = 24 words
    private const int BitsPerWord = 11;         // Each word represents 11 bits

    private const int Pbkdf2Iterations = 2048;
    private const int SeedLengthBytes = 64;

    /// <summary>
    /// Generates a new 24-word BIP-39 mnemonic using cryptographically secure random entropy.
    /// </summary>
    /// <returns>Space-separated 24-word mnemonic phrase</returns>
    public static string GenerateMnemonic()
    {
        var entropy = new byte[EntropyBytes];
        var secureRandom = new SecureRandom();
        secureRandom.NextBytes(entropy);

        return EntropyToMnemonic(entropy);
    }

    /// <summary>
    /// Converts entropy bytes to a mnemonic phrase.
    /// </summary>
    /// <param name="entropy">32 bytes of entropy</param>
    /// <returns>Space-separated 24-word mnemonic phrase</returns>
    public static string EntropyToMnemonic(byte[] entropy)
    {
        if (entropy == null || entropy.Length != EntropyBytes)
            throw new ArgumentException($"Entropy must be exactly {EntropyBytes} bytes", nameof(entropy));

        // Calculate SHA-256 checksum
        var sha256 = new Sha256Digest();
        var hash = new byte[sha256.GetDigestSize()];
        sha256.BlockUpdate(entropy, 0, entropy.Length);
        sha256.DoFinal(hash, 0);

        // Combine entropy + first byte of checksum (we only need 8 bits for 256-bit entropy)
        var combined = new byte[EntropyBytes + 1];
        Array.Copy(entropy, 0, combined, 0, EntropyBytes);
        combined[EntropyBytes] = hash[0];

        // Convert to binary string for easier bit manipulation
        var bits = BytesToBinaryString(combined);

        // Take only the bits we need (256 entropy + 8 checksum = 264 bits)
        bits = bits.Substring(0, EntropyBits + ChecksumBits);

        // Split into 24 groups of 11 bits and convert to words
        var words = new string[WordCount];
        for (int i = 0; i < WordCount; i++)
        {
            var wordBits = bits.Substring(i * BitsPerWord, BitsPerWord);
            var index = Convert.ToInt32(wordBits, 2);
            words[i] = Bip39Wordlist.GetWord(index);
        }

        return string.Join(" ", words);
    }

    /// <summary>
    /// Validates a BIP-39 mnemonic phrase.
    /// Checks word count, all words are in wordlist, and checksum is valid.
    /// </summary>
    /// <param name="mnemonic">Space-separated mnemonic phrase</param>
    /// <returns>True if the mnemonic is valid</returns>
    public static bool ValidateMnemonic(string mnemonic)
    {
        if (string.IsNullOrWhiteSpace(mnemonic))
            return false;

        var words = mnemonic.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        // Check word count (we only support 24-word mnemonics for now)
        if (words.Length != WordCount)
            return false;

        // Check all words are in wordlist and get their indices
        var indices = new int[WordCount];
        for (int i = 0; i < WordCount; i++)
        {
            var index = Bip39Wordlist.GetIndex(words[i]);
            if (index < 0)
                return false;
            indices[i] = index;
        }

        // Reconstruct the bits
        var bitsBuilder = new StringBuilder(WordCount * BitsPerWord);
        foreach (var index in indices)
        {
            bitsBuilder.Append(Convert.ToString(index, 2).PadLeft(BitsPerWord, '0'));
        }
        var bits = bitsBuilder.ToString();

        // Extract entropy (first 256 bits) and checksum (last 8 bits)
        var entropyBits = bits.Substring(0, EntropyBits);
        var checksumBits = bits.Substring(EntropyBits, ChecksumBits);

        // Convert entropy bits back to bytes
        var entropy = BinaryStringToBytes(entropyBits);

        // Calculate expected checksum
        var sha256 = new Sha256Digest();
        var hash = new byte[sha256.GetDigestSize()];
        sha256.BlockUpdate(entropy, 0, entropy.Length);
        sha256.DoFinal(hash, 0);

        // Get first 8 bits of hash as checksum
        var expectedChecksumBits = Convert.ToString(hash[0], 2).PadLeft(8, '0');

        return checksumBits == expectedChecksumBits;
    }

    /// <summary>
    /// Converts a BIP-39 mnemonic to a 64-byte seed using PBKDF2-HMAC-SHA512.
    /// </summary>
    /// <param name="mnemonic">Space-separated mnemonic phrase</param>
    /// <param name="passphrase">Optional passphrase (default: empty string)</param>
    /// <returns>64-byte seed</returns>
    public static byte[] MnemonicToSeed(string mnemonic, string passphrase = "")
    {
        if (string.IsNullOrWhiteSpace(mnemonic))
            throw new ArgumentException("Mnemonic cannot be empty", nameof(mnemonic));

        // Normalize mnemonic: lowercase, single spaces
        var normalizedMnemonic = NormalizeMnemonic(mnemonic);

        // BIP-39 specifies: salt = "mnemonic" + passphrase
        var salt = "mnemonic" + (passphrase ?? "");

        // Convert to bytes using UTF-8 (NFKD normalization per BIP-39, but for ASCII words this is identity)
        var passwordBytes = Encoding.UTF8.GetBytes(normalizedMnemonic);
        var saltBytes = Encoding.UTF8.GetBytes(salt);

        // PBKDF2 with SHA-512
        var generator = new Pkcs5S2ParametersGenerator(new Sha512Digest());
        generator.Init(passwordBytes, saltBytes, Pbkdf2Iterations);

        var key = (KeyParameter)generator.GenerateDerivedMacParameters(SeedLengthBytes * 8);
        return key.GetKey();
    }

    /// <summary>
    /// Normalizes a mnemonic string: lowercase, trimmed, single spaces between words.
    /// </summary>
    private static string NormalizeMnemonic(string mnemonic)
    {
        var words = mnemonic.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words);
    }

    /// <summary>
    /// Converts a byte array to a binary string (each byte becomes 8 characters of 0/1).
    /// </summary>
    private static string BytesToBinaryString(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 8);
        foreach (var b in bytes)
        {
            sb.Append(Convert.ToString(b, 2).PadLeft(8, '0'));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Converts a binary string to a byte array.
    /// </summary>
    private static byte[] BinaryStringToBytes(string bits)
    {
        var numBytes = bits.Length / 8;
        var bytes = new byte[numBytes];
        for (int i = 0; i < numBytes; i++)
        {
            bytes[i] = Convert.ToByte(bits.Substring(i * 8, 8), 2);
        }
        return bytes;
    }
}
