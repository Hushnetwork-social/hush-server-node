using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace Olimpo.CredentialsManager;

/// <summary>
/// Service for exporting and importing encrypted credential backup files (.dat).
/// Uses PBKDF2 for key derivation and AES-256-GCM for encryption.
/// Compatible with both Client and Node.
/// </summary>
public class CredentialsFileService
{
    private static readonly byte[] MagicNumber = Encoding.ASCII.GetBytes("HUSH");
    private const int FileVersion = 1;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int AesKeySize = 32; // 256 bits
    private const int Pbkdf2Iterations = 100_000;
    private const int GcmTagSize = 128; // bits

    /// <summary>
    /// Exports credentials to an encrypted byte array.
    /// </summary>
    /// <param name="credentials">The credentials to export</param>
    /// <param name="password">User-provided password for encryption</param>
    /// <returns>Encrypted byte array containing the credentials</returns>
    public byte[] ExportToEncryptedBytes(PortableCredentials credentials, string password)
    {
        // Serialize credentials to JSON
        var json = JsonSerializer.Serialize(credentials);
        var plaintext = Encoding.UTF8.GetBytes(json);

        // Generate random salt and nonce
        var random = new SecureRandom();
        var salt = new byte[SaltSize];
        var nonce = new byte[NonceSize];
        random.NextBytes(salt);
        random.NextBytes(nonce);

        // Derive AES key from password using PBKDF2
        var key = DeriveKey(password, salt);

        // Encrypt with AES-256-GCM
        var cipher = new GcmBlockCipher(new AesEngine());
        var parameters = new AeadParameters(new KeyParameter(key), GcmTagSize, nonce);
        cipher.Init(true, parameters);

        var ciphertext = new byte[cipher.GetOutputSize(plaintext.Length)];
        var len = cipher.ProcessBytes(plaintext, 0, plaintext.Length, ciphertext, 0);
        cipher.DoFinal(ciphertext, len);

        // Build output: Magic + Version + Salt + Nonce + Ciphertext
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);

        writer.Write(MagicNumber);
        writer.Write(FileVersion);
        writer.Write(salt);
        writer.Write(nonce);
        writer.Write(ciphertext);

        return output.ToArray();
    }

    /// <summary>
    /// Exports credentials to an encrypted file.
    /// </summary>
    public void ExportToFile(PortableCredentials credentials, string filePath, string password)
    {
        var bytes = ExportToEncryptedBytes(credentials, password);
        File.WriteAllBytes(filePath, bytes);
    }

    /// <summary>
    /// Imports credentials from an encrypted byte array.
    /// </summary>
    /// <param name="data">Encrypted byte array</param>
    /// <param name="password">User-provided password for decryption</param>
    /// <returns>Decrypted credentials, or null if decryption fails</returns>
    public PortableCredentials? ImportFromEncryptedBytes(byte[] data, string password)
    {
        try
        {
            using var input = new MemoryStream(data);
            using var reader = new BinaryReader(input);

            // Verify magic number
            var magic = reader.ReadBytes(4);
            if (!magic.SequenceEqual(MagicNumber))
            {
                return null; // Invalid file format
            }

            // Read version
            var version = reader.ReadInt32();
            if (version != FileVersion)
            {
                return null; // Unsupported version
            }

            // Read salt, nonce, and ciphertext
            var salt = reader.ReadBytes(SaltSize);
            var nonce = reader.ReadBytes(NonceSize);
            var ciphertext = reader.ReadBytes((int)(input.Length - input.Position));

            // Derive key from password
            var key = DeriveKey(password, salt);

            // Decrypt with AES-256-GCM
            var cipher = new GcmBlockCipher(new AesEngine());
            var parameters = new AeadParameters(new KeyParameter(key), GcmTagSize, nonce);
            cipher.Init(false, parameters);

            var plaintext = new byte[cipher.GetOutputSize(ciphertext.Length)];
            var len = cipher.ProcessBytes(ciphertext, 0, ciphertext.Length, plaintext, 0);
            cipher.DoFinal(plaintext, len);

            // Deserialize JSON
            var json = Encoding.UTF8.GetString(plaintext).TrimEnd('\0');
            return JsonSerializer.Deserialize<PortableCredentials>(json);
        }
        catch
        {
            // Decryption failed (wrong password or corrupted file)
            return null;
        }
    }

    /// <summary>
    /// Imports credentials from an encrypted file.
    /// </summary>
    public PortableCredentials? ImportFromFile(string filePath, string password)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(filePath);
        return ImportFromEncryptedBytes(bytes, password);
    }

    /// <summary>
    /// Validates that a file is a valid encrypted credentials file.
    /// </summary>
    public bool IsValidCredentialsFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return false;

            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream);

            if (stream.Length < 8) // Magic + Version minimum
                return false;

            var magic = reader.ReadBytes(4);
            if (!magic.SequenceEqual(MagicNumber))
                return false;

            var version = reader.ReadInt32();
            return version == FileVersion;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        var generator = new Pkcs5S2ParametersGenerator(new Sha256Digest());
        generator.Init(
            Encoding.UTF8.GetBytes(password),
            salt,
            Pbkdf2Iterations);

        var keyParam = (KeyParameter)generator.GenerateDerivedMacParameters(AesKeySize * 8);
        return keyParam.GetKey();
    }
}
