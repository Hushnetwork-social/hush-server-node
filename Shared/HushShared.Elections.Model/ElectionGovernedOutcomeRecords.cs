namespace HushShared.Elections.Model;

public static class ElectionGovernedOutcomeConstants
{
    public const string Feat146AcceptedFeat140HandoffHash =
        "3802773c78d2a0d49822c3823dad65c65be88747f6270c1e4ce68a849328cd78";

    public const string ElectionOwnerAuthorityRole = "ElectionOwner";

    public const string Feat140AuthoritySource = "FEAT-140";
}

public record ElectionGovernedOutcomeDecisionRecord(
    Guid Id,
    ElectionId ElectionId,
    ElectionGovernedOutcomeDecisionType DecisionType,
    ElectionOutcomeStatus OutcomeStatus,
    bool CleanFinalization,
    ElectionGovernedOutcomeFinalizationMode FinalizationMode,
    ElectionLifecycleState PreviousLifecycleState,
    ElectionLifecycleState ResultingLifecycleState,
    string ActorPublicAddress,
    string AuthorityRole,
    string AuthoritySource,
    string Feat140HandoffRef,
    string Feat140HandoffHash,
    string AuthorityDecisionRef,
    string AuthorityDecisionHash,
    string GovernanceRuleRef,
    string? FinalityRuleRef,
    string? RemedyRuleRef,
    Guid CloseArtifactId,
    Guid TallyReadyArtifactId,
    Guid UnofficialResultArtifactId,
    Guid OfficialResultArtifactId,
    Guid OfficialResultSourceArtifactId,
    Guid? FinalizeArtifactId,
    IReadOnlyList<string> MissingFinalizeEvidenceRefs,
    IReadOnlyList<string> ContinuityIncidentEvidenceRefs,
    IReadOnlyList<string> AvailableTrusteeAcknowledgementRefs,
    IReadOnlyList<Guid> KeyLostTrusteeDecisionIds,
    string PublicSummary,
    DateTime DecidedAtUtc,
    DateTime RecordedAtUtc,
    Guid? SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId)
{
    public string ActorPublicAddress { get; init; } =
        NormalizeRequiredValue(ActorPublicAddress, nameof(ActorPublicAddress));

    public string AuthorityRole { get; init; } =
        NormalizeRequiredValue(AuthorityRole, nameof(AuthorityRole));

    public string AuthoritySource { get; init; } =
        NormalizeRequiredValue(AuthoritySource, nameof(AuthoritySource));

    public string Feat140HandoffRef { get; init; } =
        NormalizeRequiredValue(Feat140HandoffRef, nameof(Feat140HandoffRef));

    public string Feat140HandoffHash { get; init; } =
        NormalizeRequiredValue(Feat140HandoffHash, nameof(Feat140HandoffHash));

    public string AuthorityDecisionRef { get; init; } =
        NormalizeRequiredValue(AuthorityDecisionRef, nameof(AuthorityDecisionRef));

    public string AuthorityDecisionHash { get; init; } =
        NormalizeRequiredValue(AuthorityDecisionHash, nameof(AuthorityDecisionHash));

    public string GovernanceRuleRef { get; init; } =
        NormalizeRequiredValue(GovernanceRuleRef, nameof(GovernanceRuleRef));

    public string? FinalityRuleRef { get; init; } =
        NormalizeOptionalValue(FinalityRuleRef);

    public string? RemedyRuleRef { get; init; } =
        NormalizeOptionalValue(RemedyRuleRef);

    public Guid CloseArtifactId { get; init; } =
        RequireGuid(CloseArtifactId, nameof(CloseArtifactId));

    public Guid TallyReadyArtifactId { get; init; } =
        RequireGuid(TallyReadyArtifactId, nameof(TallyReadyArtifactId));

    public Guid UnofficialResultArtifactId { get; init; } =
        RequireGuid(UnofficialResultArtifactId, nameof(UnofficialResultArtifactId));

    public Guid OfficialResultArtifactId { get; init; } =
        RequireGuid(OfficialResultArtifactId, nameof(OfficialResultArtifactId));

    public Guid OfficialResultSourceArtifactId { get; init; } =
        RequireGuid(OfficialResultSourceArtifactId, nameof(OfficialResultSourceArtifactId));

    public Guid? FinalizeArtifactId { get; init; } =
        FinalizeArtifactId == Guid.Empty ? null : FinalizeArtifactId;

    public IReadOnlyList<string> MissingFinalizeEvidenceRefs { get; init; } =
        NormalizeReferenceList(MissingFinalizeEvidenceRefs);

    public IReadOnlyList<string> ContinuityIncidentEvidenceRefs { get; init; } =
        NormalizeReferenceList(ContinuityIncidentEvidenceRefs);

    public IReadOnlyList<string> AvailableTrusteeAcknowledgementRefs { get; init; } =
        NormalizeReferenceList(AvailableTrusteeAcknowledgementRefs);

    public IReadOnlyList<Guid> KeyLostTrusteeDecisionIds { get; init; } =
        NormalizeGuidList(KeyLostTrusteeDecisionIds);

    public string PublicSummary { get; init; } =
        NormalizeRequiredValue(PublicSummary, nameof(PublicSummary));

    public ElectionGovernedOutcomeDecisionType DecisionType { get; init; } =
        ValidateDecisionType(DecisionType);

    public ElectionOutcomeStatus OutcomeStatus { get; init; } =
        ValidateOutcomeStatus(DecisionType, OutcomeStatus);

    public bool CleanFinalization { get; init; } =
        ValidateCleanFinalization(DecisionType, CleanFinalization);

    public ElectionGovernedOutcomeFinalizationMode FinalizationMode { get; init; } =
        ValidateFinalizationMode(DecisionType, FinalizationMode);

    public ElectionLifecycleState PreviousLifecycleState { get; init; } =
        ValidatePreviousLifecycleState(DecisionType, PreviousLifecycleState);

    public ElectionLifecycleState ResultingLifecycleState { get; init; } =
        ValidateResultingLifecycleState(DecisionType, ResultingLifecycleState);

    public bool HasAbnormalOutcomeEvidence =>
        MissingFinalizeEvidenceRefs.Count > 0 ||
        ContinuityIncidentEvidenceRefs.Count > 0 ||
        KeyLostTrusteeDecisionIds.Count > 0;

    private static ElectionGovernedOutcomeDecisionType ValidateDecisionType(
        ElectionGovernedOutcomeDecisionType decisionType) =>
        decisionType == ElectionGovernedOutcomeDecisionType.AcceptFixedUnofficialResultWithAnomaly
            ? decisionType
            : throw new ArgumentOutOfRangeException(nameof(DecisionType), "Unsupported governed outcome decision type.");

    private static ElectionOutcomeStatus ValidateOutcomeStatus(
        ElectionGovernedOutcomeDecisionType decisionType,
        ElectionOutcomeStatus outcomeStatus)
    {
        if (decisionType == ElectionGovernedOutcomeDecisionType.AcceptFixedUnofficialResultWithAnomaly &&
            outcomeStatus != ElectionOutcomeStatus.FinalizedWithAnomaly)
        {
            throw new ArgumentException(
                "Accepting a fixed unofficial result with anomaly must produce finalized-with-anomaly outcome status.",
                nameof(OutcomeStatus));
        }

        return outcomeStatus;
    }

    private static bool ValidateCleanFinalization(
        ElectionGovernedOutcomeDecisionType decisionType,
        bool cleanFinalization)
    {
        if (decisionType == ElectionGovernedOutcomeDecisionType.AcceptFixedUnofficialResultWithAnomaly &&
            cleanFinalization)
        {
            throw new ArgumentException(
                "Abnormal governed outcome decisions cannot claim clean finalization.",
                nameof(CleanFinalization));
        }

        return cleanFinalization;
    }

    private static ElectionGovernedOutcomeFinalizationMode ValidateFinalizationMode(
        ElectionGovernedOutcomeDecisionType decisionType,
        ElectionGovernedOutcomeFinalizationMode finalizationMode)
    {
        if (decisionType == ElectionGovernedOutcomeDecisionType.AcceptFixedUnofficialResultWithAnomaly &&
            finalizationMode != ElectionGovernedOutcomeFinalizationMode.AbnormalFinalization)
        {
            throw new ArgumentException(
                "Accepting a fixed unofficial result with anomaly must use abnormal finalization mode.",
                nameof(FinalizationMode));
        }

        return finalizationMode;
    }

    private static ElectionLifecycleState ValidatePreviousLifecycleState(
        ElectionGovernedOutcomeDecisionType decisionType,
        ElectionLifecycleState previousLifecycleState)
    {
        if (decisionType == ElectionGovernedOutcomeDecisionType.AcceptFixedUnofficialResultWithAnomaly &&
            previousLifecycleState != ElectionLifecycleState.Closed)
        {
            throw new ArgumentException(
                "Abnormal governed outcome decisions can only start from a closed election.",
                nameof(PreviousLifecycleState));
        }

        return previousLifecycleState;
    }

    private static ElectionLifecycleState ValidateResultingLifecycleState(
        ElectionGovernedOutcomeDecisionType decisionType,
        ElectionLifecycleState resultingLifecycleState)
    {
        if (decisionType == ElectionGovernedOutcomeDecisionType.AcceptFixedUnofficialResultWithAnomaly &&
            resultingLifecycleState != ElectionLifecycleState.Finalized)
        {
            throw new ArgumentException(
                "Abnormal governed outcome decisions must keep the lifecycle result as Finalized.",
                nameof(ResultingLifecycleState));
        }

        return resultingLifecycleState;
    }

    private static Guid RequireGuid(Guid value, string paramName) =>
        value == Guid.Empty
            ? throw new ArgumentException("Value is required.", paramName)
            : value;

    private static IReadOnlyList<string> NormalizeReferenceList(IReadOnlyList<string>? values) =>
        values is null
            ? Array.Empty<string>()
            : values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

    private static IReadOnlyList<Guid> NormalizeGuidList(IReadOnlyList<Guid>? values) =>
        values is null
            ? Array.Empty<Guid>()
            : values
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToArray();

    private static string NormalizeRequiredValue(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public record ElectionTrusteeContinuityDecisionRecord(
    Guid Id,
    ElectionId ElectionId,
    string TrusteePublicAddress,
    string? TrusteeDisplayName,
    ElectionTrusteeContinuityStatus ContinuityStatus,
    string AuthorityDecisionRef,
    string AuthorityDecisionHash,
    string GovernanceRuleRef,
    IReadOnlyList<string> ContinuityEvidenceRefs,
    string RecordedByPublicAddress,
    DateTime DecidedAtUtc,
    DateTime RecordedAtUtc,
    Guid? SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId)
{
    public string TrusteePublicAddress { get; init; } =
        NormalizeRequiredValue(TrusteePublicAddress, nameof(TrusteePublicAddress));

    public string? TrusteeDisplayName { get; init; } =
        NormalizeOptionalValue(TrusteeDisplayName);

    public ElectionTrusteeContinuityStatus ContinuityStatus { get; init; } =
        ValidateContinuityStatus(ContinuityStatus);

    public string AuthorityDecisionRef { get; init; } =
        NormalizeRequiredValue(AuthorityDecisionRef, nameof(AuthorityDecisionRef));

    public string AuthorityDecisionHash { get; init; } =
        NormalizeRequiredValue(AuthorityDecisionHash, nameof(AuthorityDecisionHash));

    public string GovernanceRuleRef { get; init; } =
        NormalizeRequiredValue(GovernanceRuleRef, nameof(GovernanceRuleRef));

    public IReadOnlyList<string> ContinuityEvidenceRefs { get; init; } =
        NormalizeRequiredReferenceList(ContinuityEvidenceRefs, nameof(ContinuityEvidenceRefs));

    public string RecordedByPublicAddress { get; init; } =
        NormalizeRequiredValue(RecordedByPublicAddress, nameof(RecordedByPublicAddress));

    public bool BlocksThresholdActions =>
        ContinuityStatus == ElectionTrusteeContinuityStatus.KeyLost;

    private static ElectionTrusteeContinuityStatus ValidateContinuityStatus(
        ElectionTrusteeContinuityStatus continuityStatus) =>
        continuityStatus == ElectionTrusteeContinuityStatus.KeyLost
            ? continuityStatus
            : throw new ArgumentException(
                "Only KeyLost continuity decisions are supported in FEAT-146 v1.",
                nameof(ContinuityStatus));

    private static IReadOnlyList<string> NormalizeRequiredReferenceList(
        IReadOnlyList<string>? values,
        string paramName)
    {
        var normalized = values is null
            ? Array.Empty<string>()
            : values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        if (normalized.Length == 0)
        {
            throw new ArgumentException("At least one evidence reference is required.", paramName);
        }

        return normalized;
    }

    private static string NormalizeRequiredValue(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
