using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using HushNode.Credentials;
using HushNode.Reactions.Crypto;

namespace HushNode.Reactions;

/// <summary>
/// Service for computing user commitments from server credentials.
/// Derives a deterministic user secret from the private signing key using HKDF.
/// </summary>
public class UserCommitmentService : IUserCommitmentService
{
    private readonly IPoseidonHash _poseidonHash;
    private readonly BigInteger _localUserSecret;
    private readonly byte[] _localUserCommitment;

    // Baby JubJub subgroup order for scalar operations.
    private static readonly BigInteger CurveOrder = BigInteger.Parse(
        "21888242871839275222246405745257275088614511777268538073601725287587578984328");

    public UserCommitmentService(
        IOptions<CredentialsProfile> credentials,
        IPoseidonHash poseidonHash)
    {
        _poseidonHash = poseidonHash;

        // Derive user secret from private signing key
        var privateKey = credentials.Value.PrivateSigningKey;
        _localUserSecret = DeriveUserSecret(privateKey);

        // Compute commitment
        _localUserCommitment = ComputeCommitment(_localUserSecret);
    }

    public byte[] GetLocalUserCommitment() => _localUserCommitment;

    public BigInteger GetLocalUserSecret() => _localUserSecret;

    public byte[] ComputeCommitment(BigInteger userSecret)
    {
        // commitment = Poseidon(userSecret)
        var commitmentBigInt = _poseidonHash.Hash(userSecret);

        // Convert to 32 bytes (big-endian)
        return BigIntegerToBytes32(commitmentBigInt);
    }

    /// <summary>
    /// Derives a user secret from the private signing key using HKDF.
    /// This produces a deterministic secret that can be used across sessions.
    /// </summary>
    private static BigInteger DeriveUserSecret(string privateKeyHex)
    {
        var privateKeyBytes = Convert.FromHexString(privateKeyHex);

        // Use HKDF to derive a 256-bit secret
        // Salt: "hush-network-reactions"
        // Info: "user-secret-v1"
        var salt = Encoding.UTF8.GetBytes("hush-network-reactions");
        var info = Encoding.UTF8.GetBytes("user-secret-v1");

        var derivedBytes = DeriveHkdfSha256(privateKeyBytes, 32, salt, info);

        // Convert to BigInteger and reduce modulo curve order
        var secret = new BigInteger(derivedBytes, isUnsigned: true, isBigEndian: true);
        var reduced = secret % CurveOrder;

        // Ensure non-zero
        return reduced == 0 ? BigInteger.One : reduced;
    }

    /// <summary>
    /// Derives a deterministic commitment from a public address.
    /// Uses HKDF to derive a secret from the address, then computes Poseidon hash.
    /// </summary>
    public byte[] DeriveCommitmentFromAddress(string publicAddress)
    {
        // Derive a deterministic secret from the public address
        var addressBytes = Encoding.UTF8.GetBytes(publicAddress);

        var salt = Encoding.UTF8.GetBytes("hush-network-address-commitment");
        var info = Encoding.UTF8.GetBytes("address-secret-v1");

        var derivedBytes = DeriveHkdfSha256(addressBytes, 32, salt, info);

        // Convert to BigInteger and reduce modulo curve order
        var secret = new BigInteger(derivedBytes, isUnsigned: true, isBigEndian: true);
        var reduced = secret % CurveOrder;
        if (reduced == 0) reduced = BigInteger.One;

        // Compute commitment = Poseidon(secret)
        return ComputeCommitment(reduced);
    }

    private static byte[] BigIntegerToBytes32(BigInteger value)
    {
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);

        if (bytes.Length == 32)
            return bytes;

        if (bytes.Length < 32)
        {
            // Pad with leading zeros
            var padded = new byte[32];
            Array.Copy(bytes, 0, padded, 32 - bytes.Length, bytes.Length);
            return padded;
        }

        // Truncate if too long (shouldn't happen for field elements)
        return bytes[^32..];
    }

    private static byte[] DeriveHkdfSha256(byte[] inputKeyMaterial, int outputLength, byte[] salt, byte[] info)
    {
        using var extract = new HMACSHA256(salt);
        var prk = extract.ComputeHash(inputKeyMaterial);

        var okm = new byte[outputLength];
        var previousBlock = Array.Empty<byte>();
        byte counter = 1;
        var offset = 0;

        while (offset < outputLength)
        {
            using var expand = new HMACSHA256(prk);
            var input = new byte[previousBlock.Length + info.Length + 1];
            Buffer.BlockCopy(previousBlock, 0, input, 0, previousBlock.Length);
            Buffer.BlockCopy(info, 0, input, previousBlock.Length, info.Length);
            input[^1] = counter;

            previousBlock = expand.ComputeHash(input);
            var bytesToCopy = Math.Min(previousBlock.Length, outputLength - offset);
            Buffer.BlockCopy(previousBlock, 0, okm, offset, bytesToCopy);
            offset += bytesToCopy;
            counter++;
        }

        return okm;
    }
}
