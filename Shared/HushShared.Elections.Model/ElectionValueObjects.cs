namespace HushShared.Elections.Model;

public record OutcomeRuleDefinition(
    OutcomeRuleKind Kind,
    string TemplateKey,
    int SeatCount,
    bool BlankVoteCountsForTurnout,
    bool BlankVoteExcludedFromWinnerSelection,
    bool BlankVoteExcludedFromThresholdDenominator,
    string TieResolutionRule,
    string CalculationBasis);

public record ApprovedClientApplicationRecord(
    string ApplicationId,
    string Version);

public record ElectionOptionDefinition(
    string OptionId,
    string DisplayLabel,
    string? ShortDescription,
    int BallotOrder,
    bool IsBlankOption)
{
    public const string ReservedBlankOptionId = "blank";
    public const string ReservedBlankOptionLabel = "Blank Vote";

    public static ElectionOptionDefinition CreateReservedBlankVote(int ballotOrder) =>
        new(
            ReservedBlankOptionId,
            ReservedBlankOptionLabel,
            "Platform-reserved blank vote option.",
            ballotOrder,
            IsBlankOption: true);
}

public record ElectionMetadataSnapshot(
    string Title,
    string? ShortDescription,
    string OwnerPublicAddress,
    string? ExternalReferenceCode);

public record ElectionFrozenPolicySnapshot(
    ElectionClass ElectionClass,
    ElectionBindingStatus BindingStatus,
    ElectionGovernanceMode GovernanceMode,
    ElectionDisclosureMode DisclosureMode,
    ParticipationPrivacyMode ParticipationPrivacyMode,
    VoteUpdatePolicy VoteUpdatePolicy,
    EligibilitySourceType EligibilitySourceType,
    EligibilityMutationPolicy EligibilityMutationPolicy,
    OutcomeRuleDefinition OutcomeRule,
    IReadOnlyList<ApprovedClientApplicationRecord> ApprovedClientApplications,
    string ProtocolOmegaVersion,
    ReportingPolicy ReportingPolicy,
    ReviewWindowPolicy ReviewWindowPolicy,
    OfficialResultVisibilityPolicy OfficialResultVisibilityPolicy,
    int? RequiredApprovalCount);

public record ElectionTrusteeReference(
    string TrusteeUserAddress,
    string? TrusteeDisplayName);

public record ElectionTrusteeBoundarySnapshot(
    int RequiredApprovalCount,
    IReadOnlyList<ElectionTrusteeReference> AcceptedTrustees,
    bool EveryAcceptedTrusteeMustApprove);

public record ElectionResultOptionCount(
    string OptionId,
    string DisplayLabel,
    string? ShortDescription,
    int BallotOrder,
    int Rank,
    int VoteCount);

public record ElectionResultDenominatorEvidence(
    ElectionEligibilitySnapshotType SnapshotType,
    Guid? EligibilitySnapshotId,
    Guid? BoundaryArtifactId,
    byte[] ActiveDenominatorSetHash);
