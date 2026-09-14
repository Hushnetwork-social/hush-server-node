using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Olimpo.KeyDerivation;

namespace HushVoting.IntegrationTests.Infrastructure;

/// <summary>Independent .NET writer for the approved portable HUSH v1 contract; fixture bytes stay in memory.</summary>
internal static class HushVotingCredentialFile
{
    public static byte[] Create(DerivedKeys keys, string alias, string password, bool isPublic = false, string? mnemonic = null)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ProfileName = alias, PublicSigningAddress = keys.SigningPublicKey, PrivateSigningKey = keys.SigningPrivateKey,
            PublicEncryptAddress = keys.EncryptPublicKey, PrivateEncryptKey = keys.EncryptPrivateKey, IsPublic = isPublic, Mnemonic = mnemonic
        });
        return Encrypt(plaintext, password);
    }

    public static byte[] CreateJson(string json, string password) => Encrypt(Encoding.UTF8.GetBytes(json), password);

    private static byte[] Encrypt(byte[] plaintext, string password)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, 100_000, HashAlgorithmName.SHA256, 32);
        try
        {
            var envelope = new byte[36 + plaintext.Length + 16];
            Encoding.ASCII.GetBytes("HUSH").CopyTo(envelope, 0);
            BinaryPrimitives.WriteInt32LittleEndian(envelope.AsSpan(4, 4), 1);
            salt.CopyTo(envelope, 8);
            nonce.CopyTo(envelope, 24);
            using var cipher = new AesGcm(key, 16);
            cipher.Encrypt(nonce, plaintext, envelope.AsSpan(36, plaintext.Length), envelope.AsSpan(36 + plaintext.Length, 16));
            return envelope;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
