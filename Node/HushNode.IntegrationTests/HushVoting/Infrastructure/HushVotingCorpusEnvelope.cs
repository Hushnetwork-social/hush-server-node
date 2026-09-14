using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Olimpo.KeyDerivation;

namespace HushVoting.IntegrationTests.Infrastructure;

// FEAT-009 Phase 6 Tasks 6.9/6.10: independent bounded test oracle.
// This does not replace the actual browser/worker import validation.
internal sealed class HushVotingCorpusEnvelope : IDisposable
{
    private byte[] _plaintext;
    private JsonDocument? _document;
    public DerivedKeys Keys { get; private set; }
    public bool HasMnemonic => _document is not null && _document.RootElement.TryGetProperty("Mnemonic", out var words)
        && words.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(words.GetString());

    private HushVotingCorpusEnvelope(byte[] plaintext, JsonDocument document, DerivedKeys keys)
        => (_plaintext, _document, Keys) = (plaintext, document, keys);

    public static HushVotingCorpusEnvelope? Open(ReadOnlySpan<byte> envelope, string password)
    {
        if (envelope.Length is < 52 or > HushVotingCorpusSource.MaximumBytes
            || !envelope[..4].SequenceEqual("HUSH"u8)
            || BinaryPrimitives.ReadInt32LittleEndian(envelope[4..8]) != 1) return null;
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[] key = [], plaintext = [];
        JsonDocument? document = null;
        try
        {
            if (passwordBytes.Length > 4096) return null;
            key = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, envelope[8..24], 100_000, HashAlgorithmName.SHA256, 32);
            plaintext = new byte[envelope.Length - 52];
            using var cipher = new AesGcm(key, 16);
            cipher.Decrypt(envelope[24..36], envelope[36..^16], envelope[^16..], plaintext);
            document = JsonDocument.Parse(plaintext, new() { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            var fields = document.RootElement.EnumerateObject().ToArray();
            if (fields.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length) return null;
            string? Read(string name) => document.RootElement.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var signing = Read("PublicSigningAddress"); var signingKey = Read("PrivateSigningKey");
            var encryption = Read("PublicEncryptAddress"); var encryptionKey = Read("PrivateEncryptKey");
            if (new[] { signing, signingKey, encryption, encryptionKey }.Any(v => string.IsNullOrEmpty(v) || v.Length > 1024)) return null;
            var result = new HushVotingCorpusEnvelope(plaintext, document, new(signing!, signingKey!, encryption!, encryptionKey!));
            plaintext = []; document = null;
            return result;
        }
        catch { return null; }
        finally
        {
            document?.Dispose();
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task RegisterArtifactValuesAsync()
    {
        if (_document is null) throw new InvalidOperationException("Corpus oracle is disposed.");
        await RegisterChunksAsync(Encoding.UTF8.GetString(_plaintext));
        foreach (var property in _document.RootElement.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                var value = property.Value.GetString()!;
                if (value.Length >= 4) await RegisterChunksAsync(value);
            }
    }

    internal static async Task RegisterChunksAsync(string value)
    {
        // Overlapping bounded chunks keep large envelopes within the guard's per-value limit.
        for (var offset = 0; offset < value.Length; offset += 2048)
        {
            var chunk = value.Substring(offset, Math.Min(4096, value.Length - offset));
            if (chunk.Length >= 4) await HushVotingArtifactClient.RegisterAsync(chunk);
        }
    }

    public void Dispose()
    {
        _document?.Dispose(); _document = null;
        CryptographicOperations.ZeroMemory(_plaintext); _plaintext = [];
        Keys = null!;
    }
}
