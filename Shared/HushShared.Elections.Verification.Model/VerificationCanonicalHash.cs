using System.Security.Cryptography;
using System.Text;
using HushShared.Elections.Model;

namespace HushShared.Elections.Verification.Model;

public static class VerificationCanonicalHash
{
    public static string ComputeManifestFileSha256(byte[] bytes) =>
        ComputeSha256LowerHex(bytes);

    public static string ComputeManifestFileSha256(string content) =>
        ComputeManifestFileSha256(Encoding.UTF8.GetBytes(content ?? string.Empty));

    public static byte[] ComputeAcceptedBallotInventoryHash(
        IReadOnlyList<ElectionAcceptedBallotRecord> acceptedBallots)
    {
        ArgumentNullException.ThrowIfNull(acceptedBallots);

        return SHA256.HashData(Encoding.UTF8.GetBytes(BuildAcceptedBallotInventoryPayload(acceptedBallots)));
    }

    public static string BuildAcceptedBallotInventoryPayload(
        IReadOnlyList<ElectionAcceptedBallotRecord> acceptedBallots)
    {
        ArgumentNullException.ThrowIfNull(acceptedBallots);

        return string.Join(
            '\n',
            acceptedBallots
                .OrderBy(x => x.BallotNullifier, StringComparer.Ordinal)
                .Select(x => $"{x.BallotNullifier}|{ComputeSha256UpperHex(x.EncryptedBallotPackage)}|{ComputeSha256UpperHex(x.ProofBundle)}"));
    }

    public static byte[] ComputePublishedBallotStreamHash(
        IReadOnlyList<ElectionPublishedBallotRecord> publishedBallots)
    {
        ArgumentNullException.ThrowIfNull(publishedBallots);

        return SHA256.HashData(Encoding.UTF8.GetBytes(BuildPublishedBallotStreamPayload(publishedBallots)));
    }

    public static string BuildPublishedBallotStreamPayload(
        IReadOnlyList<ElectionPublishedBallotRecord> publishedBallots)
    {
        ArgumentNullException.ThrowIfNull(publishedBallots);

        return string.Join(
            '\n',
            publishedBallots
                .OrderBy(x => x.PublicationSequence)
                .Select(x => $"{x.PublicationSequence}|{ComputeSha256UpperHex(x.EncryptedBallotPackage)}|{ComputeSha256UpperHex(x.ProofBundle)}"));
    }

    public static string ComputeSha256LowerHex(string content) =>
        ComputeSha256LowerHex(Encoding.UTF8.GetBytes(content ?? string.Empty));

    public static string ComputeSha256LowerHex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes ?? [])).ToLowerInvariant();

    public static string ComputeSha256UpperHex(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content ?? string.Empty)));
}

