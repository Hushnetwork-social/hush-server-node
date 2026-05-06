namespace HushShared.Elections.Model;

public static class ElectionSp06ControlDomainPolicy
{
    public const int HighAssuranceV1TrusteeCount = 5;
    public const int HighAssuranceV1Threshold = 3;

    public static ElectionTrusteeControlDomainSummaryRecord EvaluateHighAssuranceV1(
        ElectionRecord election,
        ElectionCeremonyProfileRecord? thresholdProfile,
        IReadOnlyList<ElectionTrusteeReference> requiredTrustees,
        IReadOnlyList<ElectionTrusteeControlDomainRecord> controlDomains)
    {
        ArgumentNullException.ThrowIfNull(election);

        var normalizedRequiredTrustees = NormalizeTrustees(requiredTrustees);
        var normalizedControlDomains = controlDomains?.ToArray() ?? Array.Empty<ElectionTrusteeControlDomainRecord>();
        var blockers = new List<ElectionTrusteeControlDomainReadinessBlockerRecord>();

        AddProfileBlockers(election, thresholdProfile, normalizedRequiredTrustees, blockers);
        AddControlDomainBlockers(normalizedRequiredTrustees, normalizedControlDomains, blockers);

        var rows = BuildRows(normalizedRequiredTrustees, normalizedControlDomains);
        return new ElectionTrusteeControlDomainSummaryRecord(
            election.ElectionId,
            ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1,
            ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1Version,
            ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
            HighAssuranceV1TrusteeCount,
            HighAssuranceV1Threshold,
            normalizedControlDomains.Count(x => x.AcceptedBeforeOpen),
            normalizedControlDomains.Count(x => x.EvidenceStatus == ElectionTrusteeControlDomainEvidenceStatus.Accepted),
            rows.Count(x => x.EvidenceStatus == ElectionTrusteeControlDomainEvidenceStatus.Missing),
            rows.Count(x => x.EvidenceStatus == ElectionTrusteeControlDomainEvidenceStatus.Stale),
            rows.Count(x => x.EvidenceStatus == ElectionTrusteeControlDomainEvidenceStatus.Incompatible),
            IsReadyForOpen: blockers.All(x => !x.BlocksOpen),
            rows,
            blockers);
    }

    private static void AddProfileBlockers(
        ElectionRecord election,
        ElectionCeremonyProfileRecord? thresholdProfile,
        IReadOnlyList<ElectionTrusteeReference> requiredTrustees,
        List<ElectionTrusteeControlDomainReadinessBlockerRecord> blockers)
    {
        if (election.GovernanceMode != ElectionGovernanceMode.TrusteeThreshold)
        {
            blockers.Add(CreateBlocker(
                "sp06_governance_mode_mismatch",
                "SP-06 high assurance requires trustee-threshold governance.",
                trusteeId: null,
                blocksOpen: true,
                blocksFinalization: true));
        }

        if (!string.Equals(election.SelectedProfileId, ElectionSelectableProfileCatalog.TrusteeProductionProfileId, StringComparison.Ordinal))
        {
            blockers.Add(CreateBlocker(
                "trustee_threshold_profile_mismatch",
                "SP-06 high assurance requires dkg-prod-3of5 as the threshold profile.",
                trusteeId: null,
                blocksOpen: true,
                blocksFinalization: true));
        }

        if (thresholdProfile is null ||
            thresholdProfile.TrusteeCount != HighAssuranceV1TrusteeCount ||
            thresholdProfile.RequiredApprovalCount != HighAssuranceV1Threshold ||
            thresholdProfile.DevOnly)
        {
            blockers.Add(CreateBlocker(
                "trustee_threshold_profile_mismatch",
                "SP-06 high assurance requires a production 5-trustee, 3-threshold ceremony profile.",
                trusteeId: null,
                blocksOpen: true,
                blocksFinalization: true));
        }

        if (requiredTrustees.Count != HighAssuranceV1TrusteeCount)
        {
            blockers.Add(CreateBlocker(
                "trustee_count_mismatch",
                "SP-06 high assurance requires exactly five required trustees.",
                trusteeId: null,
                blocksOpen: true,
                blocksFinalization: true));
        }
    }

    private static void AddControlDomainBlockers(
        IReadOnlyList<ElectionTrusteeReference> requiredTrustees,
        IReadOnlyList<ElectionTrusteeControlDomainRecord> controlDomains,
        List<ElectionTrusteeControlDomainReadinessBlockerRecord> blockers)
    {
        var requiredTrusteeKeys = requiredTrustees
            .Select(x => x.TrusteeUserAddress)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var domainByAccount = controlDomains
            .Where(x => requiredTrusteeKeys.Contains(x.TrusteeAccountId))
            .GroupBy(x => x.TrusteeAccountId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var trustee in requiredTrustees)
        {
            if (!domainByAccount.TryGetValue(trustee.TrusteeUserAddress, out var domain))
            {
                blockers.Add(CreateBlocker(
                    "control_domain_evidence_missing",
                    "Required trustee control-domain evidence is missing.",
                    BuildTrusteeId(trustee.TrusteeUserAddress),
                    blocksOpen: true,
                    blocksFinalization: false));
                continue;
            }

            if (!domain.AcceptedBeforeOpen ||
                domain.EvidenceStatus != ElectionTrusteeControlDomainEvidenceStatus.Accepted)
            {
                blockers.Add(CreateBlocker(
                    "trustee_acceptance_incomplete",
                    "Trustee control-domain evidence must be accepted before open.",
                    domain.TrusteeId,
                    blocksOpen: true,
                    blocksFinalization: false));
            }

            if (!ElectionSp06ProfileIds.IsHighAssuranceV1AllowedCustodyMode(domain.CustodyMode))
            {
                blockers.Add(CreateBlocker(
                    "trustee_custody_mode_unsupported",
                    "Trustee custody mode is not allowed for SP-06 high assurance.",
                    domain.TrusteeId,
                    blocksOpen: true,
                    blocksFinalization: true));
            }
        }

        AddDuplicateBlockers(controlDomains, x => x.TrusteeAccountId, "trustee_duplicate_account", blockers);
        AddDuplicateBlockers(controlDomains, x => x.TrusteePersonRef, "trustee_duplicate_person", blockers);
        AddDuplicateBlockers(controlDomains, x => x.CustodyDomainRefHash, "trustee_duplicate_custody_domain", blockers);

        var adminThresholdDomain = controlDomains
            .GroupBy(x => x.AdminDomainRefHash, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() >= HighAssuranceV1Threshold);
        if (adminThresholdDomain is not null)
        {
            blockers.Add(CreateBlocker(
                "trustee_admin_domain_threshold_violation",
                "One admin/operator domain can control enough trustees to meet the threshold.",
                trusteeId: null,
                blocksOpen: true,
                blocksFinalization: true));
        }
    }

    private static void AddDuplicateBlockers(
        IReadOnlyList<ElectionTrusteeControlDomainRecord> controlDomains,
        Func<ElectionTrusteeControlDomainRecord, string> selector,
        string code,
        List<ElectionTrusteeControlDomainReadinessBlockerRecord> blockers)
    {
        foreach (var duplicate in controlDomains
            .GroupBy(selector, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1))
        {
            blockers.Add(CreateBlocker(
                code,
                $"SP-06 high assurance requires distinct trustee evidence for {code}.",
                trusteeId: null,
                blocksOpen: true,
                blocksFinalization: true));
        }
    }

    private static IReadOnlyList<ElectionTrusteeControlDomainSummaryRowRecord> BuildRows(
        IReadOnlyList<ElectionTrusteeReference> requiredTrustees,
        IReadOnlyList<ElectionTrusteeControlDomainRecord> controlDomains)
    {
        var domainByAccount = controlDomains
            .GroupBy(x => x.TrusteeAccountId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        return requiredTrustees
            .Select(trustee =>
            {
                if (!domainByAccount.TryGetValue(trustee.TrusteeUserAddress, out var domain))
                {
                    return new ElectionTrusteeControlDomainSummaryRowRecord(
                        BuildTrusteeId(trustee.TrusteeUserAddress),
                        BuildTrusteePseudonym(trustee.TrusteeUserAddress),
                        ElectionTrusteeControlDomainEvidenceStatus.Missing,
                        AcceptedBeforeOpen: false,
                        AcceptedAt: null,
                        PublicKeyCommitmentHash: null,
                        CustodyDomainEvidenceHash: null,
                        AdminDomainEvidenceHash: null,
                        ElectionTrusteeBackupStatus.Missing,
                        ElectionTrusteeExceptionStatus.None,
                        FailureCode: "control_domain_evidence_missing");
                }

                return new ElectionTrusteeControlDomainSummaryRowRecord(
                    domain.TrusteeId,
                    BuildTrusteePseudonym(domain.TrusteeAccountId),
                    domain.EvidenceStatus,
                    domain.AcceptedBeforeOpen,
                    domain.AcceptedAt,
                    domain.PublicKeyCommitmentHash,
                    domain.CustodyDomainRefHash,
                    domain.AdminDomainRefHash,
                    domain.BackupStatus,
                    domain.ExceptionStatus,
                    domain.EvidenceFailureCode);
            })
            .ToArray();
    }

    private static IReadOnlyList<ElectionTrusteeReference> NormalizeTrustees(
        IReadOnlyList<ElectionTrusteeReference>? trustees) =>
        trustees is null
            ? Array.Empty<ElectionTrusteeReference>()
            : trustees
                .Where(x => !string.IsNullOrWhiteSpace(x.TrusteeUserAddress))
                .GroupBy(x => x.TrusteeUserAddress.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(x => new ElectionTrusteeReference(x.Key, x.First().TrusteeDisplayName))
                .OrderBy(x => x.TrusteeUserAddress, StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static ElectionTrusteeControlDomainReadinessBlockerRecord CreateBlocker(
        string code,
        string message,
        string? trusteeId,
        bool blocksOpen,
        bool blocksFinalization) =>
        new(code, message, trusteeId, blocksOpen, blocksFinalization);

    private static string BuildTrusteeId(string trusteeUserAddress) =>
        $"trustee-{ComputeStableHash(trusteeUserAddress)[..12]}";

    private static string BuildTrusteePseudonym(string trusteeUserAddress) =>
        $"trustee-ref-{ComputeStableHash(trusteeUserAddress)[..12]}";

    private static string ComputeStableHash(string value) =>
        Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant())))
            .ToLowerInvariant();
}
