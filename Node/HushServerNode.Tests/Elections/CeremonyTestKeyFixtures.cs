using System.Security.Cryptography;
using HushNode.Reactions.Crypto;

namespace HushServerNode.Tests.Elections;

internal static class CeremonyTestKeyFixtures
{
    public static readonly byte[] PublicKeyBytes = new BabyJubJubCurve().Generator.ToBytes();
    public static readonly string Fingerprint = Convert.ToHexString(SHA256.HashData(PublicKeyBytes));
}
