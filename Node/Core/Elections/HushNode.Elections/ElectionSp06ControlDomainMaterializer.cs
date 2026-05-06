using System.Security.Cryptography;
using System.Text;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;

namespace HushNode.Elections;

public static class ElectionSp06ControlDomainMaterializer
{
    public static IReadOnlyList<ElectionTrusteeControlDomainRecord> BuildFromCeremonyEvidence(
        ElectionRecord election,
        ElectionCeremonyVersionRecord? ceremonyVersion,
        IReadOnlyList<ElectionTrusteeInvitationRecord> trusteeInvitations,
        IReadOnlyList<ElectionCeremonyTrusteeStateRecord> trusteeStates,
        IReadOnlyList<ElectionCeremonyShareCustodyRecord> shareCustodyRecords,
        DateTime? recordedAt = null)
    {
        ArgumentNullException.ThrowIfNull(election);

        if (!IsHighAssuranceClaimed(election) || ceremonyVersion is null)
        {
            return Array.Empty<ElectionTrusteeControlDomainRecord>();
        }

        var acceptedInvitations = (trusteeInvitations ?? Array.Empty<ElectionTrusteeInvitationRecord>())
            .Where(x => x.Status == ElectionTrusteeInvitationStatus.Accepted)
            .GroupBy(x => NormalizeAddress(x.TrusteeUserAddress), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var stateByTrustee = (trusteeStates ?? Array.Empty<ElectionCeremonyTrusteeStateRecord>())
            .Where(x => x.CeremonyVersionId == ceremonyVersion.Id)
            .GroupBy(x => NormalizeAddress(x.TrusteeUserAddress), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var custodyByTrustee = (shareCustodyRecords ?? Array.Empty<ElectionCeremonyShareCustodyRecord>())
            .Where(x => x.CeremonyVersionId == ceremonyVersion.Id)
            .GroupBy(x => NormalizeAddress(x.TrusteeUserAddress), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var materialized = new List<ElectionTrusteeControlDomainRecord>();

        foreach (var trustee in ceremonyVersion.BoundTrustees
            .Where(x => !string.IsNullOrWhiteSpace(x.TrusteeUserAddress))
            .OrderBy(x => x.TrusteeUserAddress, StringComparer.OrdinalIgnoreCase))
        {
            var trusteeAddress = NormalizeAddress(trustee.TrusteeUserAddress);
            if (!acceptedInvitations.TryGetValue(trusteeAddress, out var invitation) ||
                !stateByTrustee.TryGetValue(trusteeAddress, out var state) ||
                state.State != ElectionTrusteeCeremonyState.CeremonyCompleted ||
                string.IsNullOrWhiteSpace(state.ShareVersion) ||
                state.CloseCountingPublicCommitment is not { Length: > 0 } ||
                !custodyByTrustee.TryGetValue(trusteeAddress, out var custody))
            {
                continue;
            }

            var acceptedAt = state.CompletedAt ??
                invitation.RespondedAt ??
                ceremonyVersion.CompletedAt ??
                ceremonyVersion.StartedAt;
            var acceptedBeforeOpen = !election.OpenedAt.HasValue || acceptedAt <= election.OpenedAt.Value;
            var (evidenceStatus, failureCode, failureReason) = ResolveEvidenceStatus(
                acceptedBeforeOpen,
                state,
                custody);
            var publicKeyCommitmentHash = Convert.ToHexString(SHA256.HashData(state.CloseCountingPublicCommitment));
            var sourceSeed = $"{election.ElectionId}|{ceremonyVersion.Id:N}|{trusteeAddress}";

            materialized.Add(new ElectionTrusteeControlDomainRecord(
                BuildDeterministicGuid("sp06-control-domain", sourceSeed),
                election.ElectionId,
                ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1,
                ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1Version,
                ceremonyVersion.ProfileId,
                ceremonyVersion.Id,
                BuildTrusteeId(trusteeAddress),
                trusteeAddress,
                BuildStableHash($"sp06-person|{trusteeAddress}"),
                IsOwnerTrustee(election, trusteeAddress)
                    ? ElectionTrusteeRole.OwnerTrustee
                    : ElectionTrusteeRole.InternalTrustee,
                ElectionSp06ProfileIds.TrusteeLocalSecureVaultV1,
                BuildStableHash(
                    $"sp06-custody|{sourceSeed}|{state.ShareVersion}|{state.TransportPublicKeyFingerprint}|{publicKeyCommitmentHash}"),
                BuildStableHash($"sp06-admin-domain|{trusteeAddress}"),
                LegalEntityRefHash: null,
                publicKeyCommitmentHash,
                acceptedAt,
                acceptedBeforeOpen,
                ElectionTrusteeBackupStatus.Registered,
                ElectionTrusteeExceptionStatus.None,
                evidenceStatus,
                failureCode,
                failureReason,
                recordedAt ?? acceptedAt,
                invitation.InvitedByPublicAddress,
                invitation.LatestTransactionId,
                invitation.LatestBlockHeight,
                invitation.LatestBlockId));
        }

        return materialized;
    }

    private static (ElectionTrusteeControlDomainEvidenceStatus Status, string? Code, string? Reason) ResolveEvidenceStatus(
        bool acceptedBeforeOpen,
        ElectionCeremonyTrusteeStateRecord trusteeState,
        ElectionCeremonyShareCustodyRecord custodyRecord)
    {
        if (custodyRecord.Status == ElectionCeremonyShareCustodyStatus.ImportFailed)
        {
            return (
                ElectionTrusteeControlDomainEvidenceStatus.Incompatible,
                "trustee_custody_import_failed",
                custodyRecord.LastImportFailureReason ?? "Trustee share custody import failed.");
        }

        if (!string.Equals(custodyRecord.ShareVersion, trusteeState.ShareVersion, StringComparison.Ordinal))
        {
            return (
                ElectionTrusteeControlDomainEvidenceStatus.Incompatible,
                "trustee_custody_share_version_mismatch",
                "Trustee custody share version does not match the completed ceremony state.");
        }

        if (!acceptedBeforeOpen)
        {
            return (
                ElectionTrusteeControlDomainEvidenceStatus.Stale,
                "trustee_acceptance_after_open",
                "Trustee control-domain evidence was completed after election open.");
        }

        return (ElectionTrusteeControlDomainEvidenceStatus.Accepted, null, null);
    }

    private static bool IsHighAssuranceClaimed(ElectionRecord election) =>
        string.Equals(
            election.ControlDomainProfileId,
            ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1,
            StringComparison.Ordinal);

    private static bool IsOwnerTrustee(ElectionRecord election, string trusteeAddress) =>
        string.Equals(election.OwnerPublicAddress, trusteeAddress, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeAddress(string value) =>
        value.Trim().ToLowerInvariant();

    private static string BuildTrusteeId(string trusteeUserAddress) =>
        $"trustee-{BuildStableHash(trusteeUserAddress)[..12].ToLowerInvariant()}";

    private static string BuildStableHash(string value) =>
        VerificationCanonicalHash.ComputeSha256UpperHex(value.Trim().ToLowerInvariant());

    private static Guid BuildDeterministicGuid(string scope, string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}|{value}".Trim().ToLowerInvariant()));
        return new Guid(bytes[..16]);
    }
}
