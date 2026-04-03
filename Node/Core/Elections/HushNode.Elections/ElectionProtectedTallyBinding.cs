using System.Security.Cryptography;
using System.Text;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public static class ElectionProtectedTallyBinding
{
    public const string AdminOnlyProtectedCustodyProfileId = "admin-only-protected-custody-v1";

    public static ElectionCeremonyBindingSnapshot? ResolveBoundaryBinding(
        ElectionRecord election,
        ElectionBoundaryArtifactRecord? boundaryArtifact)
    {
        if (boundaryArtifact?.CeremonySnapshot is not null)
        {
            return boundaryArtifact.CeremonySnapshot;
        }

        return election.GovernanceMode == ElectionGovernanceMode.AdminOnly
            ? BuildAdminOnlyProtectedTallyBindingSnapshot(election)
            : null;
    }

    public static ElectionCeremonyBindingSnapshot? ResolveOpenBoundaryBinding(
        ElectionRecord election,
        ElectionBoundaryArtifactRecord? openArtifact) =>
        ResolveBoundaryBinding(election, openArtifact);

    public static ElectionCeremonyBindingSnapshot BuildAdminOnlyProtectedTallyBindingSnapshot(
        ElectionRecord election)
    {
        var bindingSeed =
            $"{election.ProtocolOmegaVersion}:admin-only-protected-custody:{election.ElectionId}:{election.CurrentDraftRevision}:{election.OwnerPublicAddress}";

        return ElectionModelFactory.CreateCeremonyBindingSnapshot(
            CreateDeterministicGuid($"{bindingSeed}:binding"),
            ceremonyVersionNumber: 1,
            profileId: AdminOnlyProtectedCustodyProfileId,
            boundTrusteeCount: 1,
            requiredApprovalCount: 1,
            activeTrustees:
            [
                new ElectionTrusteeReference(election.OwnerPublicAddress, null),
            ],
            tallyPublicKeyFingerprint: ComputeScopedHash($"{bindingSeed}:tally-public-key"));
    }

    private static string ComputeScopedHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", nameof(value));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim())));
    }

    private static Guid CreateDeterministicGuid(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", nameof(value));
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return new Guid(hash.AsSpan(0, 16));
    }
}
