using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public static partial class ElectionEligibilityContracts
{
    public const string TemporaryVerificationCode = "1111";
    public const string RosterCanonicalizationVersionHash =
        "a99e986c6935587c6a75ebf5097e4f3ed52e8749a378588532b77de2ec3c5c34";
    public const string EligibilityPolicyCanonicalizationVersionHash =
        "d2ce2c975af0a32f0e8ad792ed977d0635ed341e57806e36cf8dd99a5944f05a";
    public const string CommitmentSchemeVersionHash =
        "6fd26bebeecc334ef432f9b0f94cda3e0945a5a95f6d75cb54d0d3b4f33e87e0";
    public const string NullifierSchemeVersionHash =
        "a24b546ac3779c7f51356ee24a428062a9fb3946014ac99979218c6871c43c4c";

    private static readonly Regex E164PhonePattern = new(@"^\+[0-9]{1,15}$", RegexOptions.Compiled);

    public static IReadOnlyList<string> ValidateRosterImportEntries(
        IReadOnlyList<ElectionRosterImportItem> rosterEntries) =>
        AnalyzeRosterImportEntries(
                ElectionId.NewElectionId,
                rosterEntries,
                existingRosterEntries: Array.Empty<ElectionRosterEntryRecord>(),
                rosterImportVersion: 1,
                importedByActor: "validator",
                importedAt: DateTime.UnixEpoch)
            .ValidationErrors;

    public static ElectionRosterImportAnalysis AnalyzeRosterImportEntries(
        ElectionId electionId,
        IReadOnlyList<ElectionRosterImportItem> rosterEntries,
        IReadOnlyList<ElectionRosterEntryRecord> existingRosterEntries,
        int rosterImportVersion,
        string importedByActor,
        DateTime importedAt)
    {
        ArgumentNullException.ThrowIfNull(rosterEntries);
        ArgumentNullException.ThrowIfNull(existingRosterEntries);

        var sourceHash = ComputeSha256LowerHex(BuildSourceRosterPayload(rosterEntries));
        var rejectedRows = new List<ElectionRosterRejectedRowRecord>();
        var validRows = new List<NormalizedRosterImportRow>();
        var existingIds = existingRosterEntries
            .Select(x => x.OrganizationVoterId.Trim())
            .ToHashSet(StringComparer.Ordinal);

        if (rosterEntries.Count == 0)
        {
            rejectedRows.Add(new ElectionRosterRejectedRowRecord(
                SourceRowNumber: 0,
                OrganizationVoterId: string.Empty,
                ReasonCode: "empty_roster",
                Reason: "At least one roster entry is required.",
                RestrictedRowValues: new Dictionary<string, string>()));
        }

        var duplicateImportIds = rosterEntries
            .Select((entry, index) => new
            {
                RowNumber = index + 1,
                OrganizationVoterId = entry.OrganizationVoterId?.Trim() ?? string.Empty,
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.OrganizationVoterId))
            .GroupBy(x => x.OrganizationVoterId, StringComparer.Ordinal)
            .Where(x => x.Count() > 1)
            .SelectMany(x => x.Select(row => row.RowNumber))
            .ToHashSet();

        for (var index = 0; index < rosterEntries.Count; index++)
        {
            var entry = rosterEntries[index];
            var rowNumber = index + 1;
            var rowErrors = new List<(string Code, string Reason)>();
            var organizationVoterId = entry.OrganizationVoterId?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(organizationVoterId))
            {
                rowErrors.Add(("missing_organization_voter_id", "Roster entry is missing organization_voter_id."));
            }
            else
            {
                if (duplicateImportIds.Contains(rowNumber))
                {
                    rowErrors.Add(("duplicate_organization_voter_id", "Duplicate organization_voter_id in import."));
                }

                if (existingIds.Contains(organizationVoterId))
                {
                    rowErrors.Add(("existing_organization_voter_id", "organization_voter_id already exists in this election roster."));
                }
            }

            if (!TryNormalizeContact(
                    entry.ContactType,
                    entry.ContactValue,
                    out var canonicalContactValue,
                    out var contactMatchKey,
                    out var contactError))
            {
                rowErrors.Add(contactError);
            }

            if (rowErrors.Count > 0)
            {
                rejectedRows.Add(new ElectionRosterRejectedRowRecord(
                    SourceRowNumber: rowNumber,
                    OrganizationVoterId: organizationVoterId,
                    ReasonCode: string.Join("+", rowErrors.Select(x => x.Code)),
                    Reason: string.Join(" ", rowErrors.Select(x => x.Reason)),
                    RestrictedRowValues: BuildRestrictedRowValues(entry)));
                continue;
            }

            validRows.Add(new NormalizedRosterImportRow(
                rowNumber,
                organizationVoterId,
                entry.ContactType,
                canonicalContactValue,
                contactMatchKey,
                entry.IsInitiallyActive));
        }

        var duplicateContactWarnings = rejectedRows.Count == 0
            ? BuildDuplicateContactWarnings(validRows)
            : Array.Empty<ElectionRosterDuplicateContactWarningRecord>();
        var acceptedRows = rejectedRows.Count == 0
            ? validRows
                .OrderBy(x => x.OrganizationVoterId, StringComparer.Ordinal)
                .Select(x => ElectionModelFactory.CreateRosterEntry(
                    electionId,
                    x.OrganizationVoterId,
                    x.ContactType,
                    x.CanonicalContactValue,
                    x.IsInitiallyActive
                        ? ElectionVotingRightStatus.Active
                        : ElectionVotingRightStatus.Inactive,
                    importedAt))
                .ToArray()
            : Array.Empty<ElectionRosterEntryRecord>();
        var canonicalHash = ComputeRosterCanonicalHash(acceptedRows);
        var evidence = ElectionModelFactory.CreateRosterImportEvidence(
            electionId,
            rosterImportVersion,
            sourceHash,
            canonicalHash,
            ElectionSp05ProfileIds.RosterCanonicalizationV1,
            RosterCanonicalizationVersionHash,
            acceptedRows.Length,
            rejectedRows.Count,
            rejectedRows.Count(x => !x.ReasonCode.Contains("duplicate_organization_voter_id", StringComparison.Ordinal)),
            rejectedRows.Count(x => x.ReasonCode.Contains("duplicate_organization_voter_id", StringComparison.Ordinal)),
            duplicateContactWarnings.Count,
            importedByActor,
            rejectedRows,
            duplicateContactWarnings,
            importedAt);
        var validationErrors = rejectedRows
            .Select(x => $"Roster row {x.SourceRowNumber}: {x.Reason}")
            .ToArray();

        return new ElectionRosterImportAnalysis(validationErrors, acceptedRows, evidence);
    }

    public static string ComputeRosterCanonicalHash(IReadOnlyList<ElectionRosterEntryRecord> rosterEntries)
    {
        ArgumentNullException.ThrowIfNull(rosterEntries);

        var lines = rosterEntries
            .OrderBy(x => x.OrganizationVoterId, StringComparer.Ordinal)
            .Select(x => string.Join(
                '\t',
                EncodeField(x.OrganizationVoterId),
                x.ContactType.ToString().ToLowerInvariant(),
                EncodeField(x.ContactValue),
                x.VotingRightStatus == ElectionVotingRightStatus.Active ? "active" : "inactive"));

        return ComputeSha256LowerHex($"HUSH_ROSTER_CANONICAL_V1\n{string.Join('\n', lines)}");
    }

    public static string ComputeEligibilityPolicyCanonicalHash(ElectionRecord election)
    {
        var payload = string.Join(
            '\n',
            "HUSH_ELIGIBILITY_POLICY_CANONICAL_V1",
            election.ElectionId.ToString(),
            election.EligibilityMutationPolicy,
            election.IdentityLinkPolicy,
            election.CheckoffVisibilityPolicy,
            election.ActorLinkMultiplicityPolicy,
            election.ContactCodeProviderReadiness);

        return ComputeSha256LowerHex(payload);
    }

    private static bool TryNormalizeContact(
        ElectionRosterContactType contactType,
        string? contactValue,
        out string canonicalContactValue,
        out string contactMatchKey,
        out (string Code, string Reason) error)
    {
        canonicalContactValue = string.Empty;
        contactMatchKey = string.Empty;

        if (!Enum.IsDefined(contactType))
        {
            error = ("unsupported_contact_type", "Roster entry has an unsupported contact type.");
            return false;
        }

        var trimmed = contactValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            error = ("missing_contact_value", "Roster entry is missing the imported contact value.");
            return false;
        }

        if (contactType == ElectionRosterContactType.Email)
        {
            var normalized = trimmed.Normalize(NormalizationForm.FormC);
            if (normalized.Any(x => char.IsControl(x) || char.IsWhiteSpace(x)))
            {
                error = ("invalid_email", "Email contact must not contain whitespace or control characters.");
                return false;
            }

            var atIndex = normalized.IndexOf('@');
            if (atIndex <= 0 || atIndex != normalized.LastIndexOf('@') || atIndex == normalized.Length - 1)
            {
                error = ("invalid_email", "Email contact must contain exactly one @ with non-empty local and domain parts.");
                return false;
            }

            var local = normalized[..atIndex];
            var domain = normalized[(atIndex + 1)..].ToLowerInvariant();
            canonicalContactValue = $"{local}@{domain}";
            contactMatchKey = $"{local.ToLowerInvariant()}@{domain}";
            error = default;
            return true;
        }

        if (contactType == ElectionRosterContactType.Phone)
        {
            if (!E164PhonePattern.IsMatch(trimmed))
            {
                error = ("invalid_phone", "Phone contact must be E.164: + followed by up to 15 digits.");
                return false;
            }

            canonicalContactValue = trimmed;
            contactMatchKey = trimmed;
            error = default;
            return true;
        }

        error = ("unsupported_contact_type", "Roster entry has an unsupported contact type.");
        return false;
    }

    private static IReadOnlyList<ElectionRosterDuplicateContactWarningRecord> BuildDuplicateContactWarnings(
        IReadOnlyList<NormalizedRosterImportRow> rows) =>
        rows
            .GroupBy(x => (x.ContactType, x.ContactMatchKey))
            .Where(x => x.Count() > 1)
            .Select(x => new ElectionRosterDuplicateContactWarningRecord(
                x.Key.ContactType,
                x.Key.ContactMatchKey,
                x.Select(row => row.OrganizationVoterId).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                WarningCode: "duplicate_contact",
                Warning: "Multiple accepted roster entries share the same contact channel."))
            .ToArray();

    private static IReadOnlyDictionary<string, string> BuildRestrictedRowValues(ElectionRosterImportItem entry) =>
        new Dictionary<string, string>
        {
            ["organization_voter_id"] = entry.OrganizationVoterId ?? string.Empty,
            ["contact_type"] = entry.ContactType.ToString(),
            ["contact_value"] = entry.ContactValue ?? string.Empty,
            ["initial_voting_right_status"] = entry.IsInitiallyActive ? "active" : "inactive",
        };

    private static string BuildSourceRosterPayload(IReadOnlyList<ElectionRosterImportItem> rosterEntries)
    {
        var lines = rosterEntries.Select((entry, index) => string.Join(
            '\t',
            index + 1,
            EncodeField(entry.OrganizationVoterId),
            entry.ContactType.ToString(),
            EncodeField(entry.ContactValue),
            entry.IsInitiallyActive ? "active" : "inactive"));

        return $"HUSH_ROSTER_IMPORT_SOURCE_V1\n{string.Join('\n', lines)}";
    }

    private static string ComputeSha256LowerHex(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content ?? string.Empty))).ToLowerInvariant();

    private static string EncodeField(string? value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

    private sealed record NormalizedRosterImportRow(
        int SourceRowNumber,
        string OrganizationVoterId,
        ElectionRosterContactType ContactType,
        string CanonicalContactValue,
        string ContactMatchKey,
        bool IsInitiallyActive);
}

public sealed record ElectionRosterImportAnalysis(
    IReadOnlyList<string> ValidationErrors,
    IReadOnlyList<ElectionRosterEntryRecord> AcceptedRosterEntries,
    ElectionRosterImportEvidenceRecord Evidence);
