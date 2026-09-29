using System.Text.Json;
using HushShared.HushVoting.Licensing.Model;

namespace HushShared.Elections.Model;

/// <summary>
/// FEAT-018: immutable, election-bound authorization evidence. Contains no voter, ballot,
/// signing address or credential. The subject reference is the existing licence subject key.
/// This is restricted audit data, not a transferable authorization token.
/// </summary>
public sealed record ElectionEntitlementCapture(
    ElectionId ElectionId,
    Guid LicenceSubjectId,
    Guid OriginatingLicenceTransactionId,
    string PlanId,
    string PlanFamily,
    int UpgradeRank,
    int? EligibleVoterCap,
    bool UnlimitedElectionPolicy,
    string TermKind,
    int TermYears,
    DateTime EffectiveFromUtc,
    DateTime? ExpiresAtUtc,
    string AllowedGovernanceOptionIdsJson,
    string AssignedCatalogueVersion,
    string AssignedCatalogueDigestSha256,
    long EntitlementRevision,
    string SelectedProfileId,
    string SelectedGovernanceOptionId,
    int FrozenEligibleVoterCount,
    Guid FrozenRosterBasisId,
    Guid OpenTransactionId,
    Guid OpenBlockId,
    long OpenBlockHeight,
    int OpenTransactionPosition,
    DateTime OpenBlockTimeUtc,
    Guid? GovernedProposalId = null,
    int SchemaVersion = 1,
    string PolicyVersion = "open-at-block/v1")
{
    public const int CurrentSchemaVersion = 1;
    public const string CurrentPolicyVersion = "open-at-block/v1";

    /// <summary>Closed structural/semantic compatibility check; never supplies missing terms.</summary>
    public bool HasSupportedSemantics()
    {
        if (SchemaVersion != CurrentSchemaVersion || PolicyVersion != CurrentPolicyVersion ||
            ElectionId == ElectionId.Empty || LicenceSubjectId == Guid.Empty ||
            OriginatingLicenceTransactionId == Guid.Empty || OpenTransactionId == Guid.Empty ||
            OpenBlockId == Guid.Empty || FrozenRosterBasisId == Guid.Empty ||
            OpenBlockHeight < 0 || OpenTransactionPosition < 0 || EntitlementRevision <= 0 ||
            UpgradeRank < 0 || FrozenEligibleVoterCount < 0 || EligibleVoterCap is <= 0 ||
            (EligibleVoterCap.HasValue && FrozenEligibleVoterCount > EligibleVoterCap.Value) ||
            !Text(PlanId) || !HushVotingLicencePlanId.Known.Any(id => id.Value == PlanId) || PlanFamily is not ("direct" or "veritas" or "enterprise") ||
            !Text(AssignedCatalogueVersion) || !Text(SelectedProfileId) || !Text(SelectedGovernanceOptionId) ||
            AssignedCatalogueDigestSha256 is null || AssignedCatalogueDigestSha256.Length != 64 ||
            !AssignedCatalogueDigestSha256.All(Uri.IsHexDigit) ||
            !HushVotingLicenceOpenInstantPolicy.IsEffectiveAt(EffectiveFromUtc, ExpiresAtUtc, OpenBlockTimeUtc) ||
            TermKind is not ("perpetual" or "annual") || TermYears < 0 ||
            (TermKind == "annual" && (TermYears == 0 || !ExpiresAtUtc.HasValue)) ||
            (TermKind == "perpetual" && (TermYears != 0 || ExpiresAtUtc.HasValue)) ||
            AllowedGovernanceOptionIdsJson is null || AllowedGovernanceOptionIdsJson.Length > 16_384)
            return false;

        try
        {
            var options = JsonSerializer.Deserialize<string[]>(AllowedGovernanceOptionIdsJson);
            return options is { Length: > 0 and <= 64 } && options.All(Text) && options.All(o => HushVotingGovernanceOptionId.TryGetKnown(o)?.Value == o) &&
                options.Distinct(StringComparer.Ordinal).Count() == options.Length &&
                options.Contains(SelectedGovernanceOptionId, StringComparer.Ordinal);
        }
        catch (JsonException) { return false; }
    }

    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256;
}

/// <summary>Durable first-link boundary. No voter identity or claim-to-ballot association.</summary>
public sealed record ElectionRosterLinkBoundary(
    ElectionId ElectionId,
    Guid SourceTransactionId,
    DateTime LinkedAtUtc);

/// <summary>Immutable negative Open outcome. Prevents replay from borrowing later rights or
/// prerequisites. Stores only closed result categories and canonical provenance, no payload,
/// actor address, roster, ballot or raw error text.</summary>
public sealed record ElectionOpenRejection(
    Guid TransactionId, ElectionId ElectionId, Guid BlockId, long BlockHeight,
    int TransactionPosition, DateTime BlockTimeUtc, Guid? GovernedProposalId,
    int ErrorCategory, ElectionEntitlementReason Reason, int SchemaVersion = 1)
{
    public bool HasSupportedSemantics() => SchemaVersion == 1 && TransactionId != Guid.Empty &&
        ElectionId != ElectionId.Empty && BlockId != Guid.Empty && BlockHeight >= 0 &&
        TransactionPosition >= 0 && BlockTimeUtc.Kind == DateTimeKind.Utc && GovernedProposalId != Guid.Empty &&
        ErrorCategory is >= 1 and <= 7 && (Reason == ElectionEntitlementReason.None ||
            (ErrorCategory == 4 && Reason is ElectionEntitlementReason.NotActive
                or ElectionEntitlementReason.LimitExceeded or ElectionEntitlementReason.ProfileNotAllowed));
}

/// <summary>Closed safe reasons, separate from the FEAT-015 licence-transaction registry.</summary>
public enum ElectionEntitlementReason
{
    None = 0,
    NotActive = 1,
    LimitExceeded = 2,
    ProfileNotAllowed = 3,
    AuthorityUnavailable = 4,
    CaptureUnavailable = 5,
    SemanticsUnsupported = 6,
    RosterReplacementAfterLink = 7,
}

public static class ElectionEntitlementReasonNames
{
    public static string ToWire(ElectionEntitlementReason reason) => reason switch
    {
        ElectionEntitlementReason.None => "",
        ElectionEntitlementReason.NotActive => "ENTITLEMENT_NOT_ACTIVE",
        ElectionEntitlementReason.LimitExceeded => "ENTITLEMENT_LIMIT_EXCEEDED",
        ElectionEntitlementReason.ProfileNotAllowed => "ENTITLEMENT_PROFILE_NOT_ALLOWED",
        ElectionEntitlementReason.AuthorityUnavailable => "ENTITLEMENT_AUTHORITY_UNAVAILABLE",
        ElectionEntitlementReason.CaptureUnavailable => "ENTITLEMENT_CAPTURE_UNAVAILABLE",
        ElectionEntitlementReason.SemanticsUnsupported => "ENTITLEMENT_SEMANTICS_UNSUPPORTED",
        ElectionEntitlementReason.RosterReplacementAfterLink => "ROSTER_REPLACEMENT_AFTER_LINK",
        _ => "ENTITLEMENT_SEMANTICS_UNSUPPORTED",
    };
}

/// <summary>Safe server projection; never includes operative licence or identity evidence.</summary>
public sealed record ElectionScopedAccessView(
    ElectionId ElectionId,
    int SchemaVersion,
    IReadOnlyList<string> AllowedOperations,
    string EntitlementReason);
