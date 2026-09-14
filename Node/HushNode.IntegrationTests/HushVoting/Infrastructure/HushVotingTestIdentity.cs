using System.Security.Cryptography;
using System.Text;
using Olimpo.KeyDerivation;

namespace HushVoting.IntegrationTests.Infrastructure;

/// <summary>P-01 test identity oracle. P-02's legacy labels and uncompressed addresses are different.</summary>
internal static class HushVotingTestIdentity
{
    public static DerivedKeys DeriveP01(string mnemonic)
    {
        var words = mnemonic.Split(' ');
        if (words.Length is not (12 or 24)) throw new InvalidOperationException("Unsupported recovery phrase count.");
        var indices = words.Select(Bip39Wordlist.GetIndex).ToArray();
        if (indices.Any(index => index < 0)) throw new InvalidOperationException("Invalid recovery vocabulary.");
        var bits = string.Concat(indices.Select(index => Convert.ToString(index, 2).PadLeft(11, '0')));
        var checksumBits = words.Length / 3;
        var entropy = Enumerable.Range(0, (bits.Length - checksumBits) / 8).Select(index => Convert.ToByte(bits.Substring(index * 8, 8), 2)).ToArray();
        var checksum = Convert.ToString(SHA256.HashData(entropy)[0], 2).PadLeft(8, '0')[..checksumBits];
        CryptographicOperations.ZeroMemory(entropy);
        if (bits[^checksumBits..] != checksum) throw new InvalidOperationException("Invalid recovery checksum.");
        var seed = MnemonicGenerator.MnemonicToSeed(mnemonic);
        var signing = HKDF.DeriveKey(HashAlgorithmName.SHA256, seed, 32, info: Encoding.UTF8.GetBytes("signing"));
        var encryption = HKDF.DeriveKey(HashAlgorithmName.SHA256, seed, 32, info: Encoding.UTF8.GetBytes("encryption"));
        try
        {
            string Public(byte[] bytes)
            {
                using var key = ECDsa.Create(new ECParameters { Curve = ECCurve.CreateFromFriendlyName("secp256k1"), D = bytes });
                var point = key.ExportParameters(false).Q;
                return ((point.Y![^1] & 1) == 0 ? "02" : "03") + Convert.ToHexString(point.X!).ToLowerInvariant();
            }
            return new(Public(signing), Convert.ToHexString(signing).ToLowerInvariant(), Public(encryption), Convert.ToHexString(encryption).ToLowerInvariant());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(seed);
            CryptographicOperations.ZeroMemory(signing);
            CryptographicOperations.ZeroMemory(encryption);
        }
    }
}
