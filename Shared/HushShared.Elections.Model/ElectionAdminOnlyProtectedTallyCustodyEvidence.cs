namespace HushShared.Elections.Model;

public static class ElectionAdminOnlyProtectedTallyCustodyReadinessIds
{
    public const string EvidenceId = "RDY-EVID-AT-RDY-002-FEAT-131-001";
    public const string DimensionId = "RDY-DIM-005";
    public const string PilotBlockerId = "RDY-BLOCK-FRIENDLY_ORGANIZATION_PILOT-001";
    public const string OpenGateId = "AT-RDY-002";
    public const string FinalizationGateId = "AT-RDY-003";
    public const string ReconciliationGateId = "AT-RDY-004";

    public static IReadOnlyList<string> RequiredGateIds { get; } =
    [
        OpenGateId,
        FinalizationGateId,
        ReconciliationGateId,
    ];
}

public static class ElectionAdminOnlyProtectedTallyCustodyResultCodes
{
    public const string OpenBound = "custody_open_bound";
    public const string OpenRetryRequired = "custody_open_retry_required";
    public const string OpenExceptionRequired = "custody_open_exception_required";
    public const string OpenProviderUnavailable = "custody_open_provider_unavailable";
    public const string OpenMissingCustodyRow = "custody_open_missing_private_row";
    public const string OpenLegacyCompatibility = "custody_open_legacy_compatibility";
    public const string OpenNotRequired = "custody_open_not_required";

    public const string FinalizationDeletionScheduled = "custody_finalization_deletion_scheduled";
    public const string FinalizationKeyDisabled = "custody_finalization_key_disabled";
    public const string FinalizationScalarDestroyed = "custody_finalization_scalar_destroyed";
    public const string FinalizationRetryRequired = "custody_finalization_retry_required";
    public const string FinalizationExceptionRequired = "custody_finalization_exception_required";
    public const string FinalizationMissingDestroyedMarker = "custody_finalization_missing_destroyed_marker";
    public const string FinalizationLegacyCompatibility = "custody_finalization_legacy_compatibility";

    public const string ReconciliationAccepted = "custody_reconciliation_accepted";
    public const string ReconciliationRetryRequired = "custody_reconciliation_retry_required";
    public const string ReconciliationExceptionRequired = "custody_reconciliation_exception_required";
    public const string ReconciliationMissing = "custody_reconciliation_missing";

    public const string PublicSecretScanPassed = "public_secret_scan_passed";
    public const string PublicSecretScanFailed = "public_secret_scan_failed";
}

public enum ElectionAdminOnlyProtectedTallyCustodyActionKind
{
    Open = 0,
    FinalizationCleanup = 1,
    Reconciliation = 2,
}

public enum ElectionAdminOnlyProtectedTallyCustodyActionResult
{
    Passed = 0,
    FailedClosed = 1,
    RetryRequired = 2,
    ExceptionRequired = 3,
}

public record ElectionAdminOnlyProtectedTallyCustodyPublicEvidence(
    string EvidenceId,
    ElectionId ElectionId,
    string SelectedProfileId,
    string CustodyMode,
    string ProviderFamily,
    string TallyPublicKeyFingerprint,
    ElectionAdminOnlyProtectedTallyCustodyLifecycleState LifecycleState,
    IReadOnlyList<string> GateIds,
    IReadOnlyList<string> PublicResultCodes,
    string PublicCustodyReferenceHash,
    string PublicRecordSecretScanStatus,
    DateTime RecordedAt)
{
    public string EvidenceId { get; init; } = NormalizeRequired(EvidenceId, nameof(EvidenceId));
    public string SelectedProfileId { get; init; } = NormalizeRequired(SelectedProfileId, nameof(SelectedProfileId));
    public string CustodyMode { get; init; } = NormalizeRequired(CustodyMode, nameof(CustodyMode));
    public string ProviderFamily { get; init; } = NormalizeRequired(ProviderFamily, nameof(ProviderFamily));
    public string TallyPublicKeyFingerprint { get; init; } =
        NormalizeRequired(TallyPublicKeyFingerprint, nameof(TallyPublicKeyFingerprint));
    public IReadOnlyList<string> GateIds { get; init; } = NormalizeRequiredList(GateIds, nameof(GateIds));
    public IReadOnlyList<string> PublicResultCodes { get; init; } =
        NormalizeRequiredList(PublicResultCodes, nameof(PublicResultCodes));
    public string PublicCustodyReferenceHash { get; init; } =
        NormalizeRequired(PublicCustodyReferenceHash, nameof(PublicCustodyReferenceHash));
    public string PublicRecordSecretScanStatus { get; init; } =
        NormalizeRequired(PublicRecordSecretScanStatus, nameof(PublicRecordSecretScanStatus));

    internal static string NormalizeRequired(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.Trim();
    }

    internal static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static IReadOnlyList<string> NormalizeRequiredList(
        IReadOnlyList<string>? values,
        string paramName)
    {
        var normalized = (values ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length == 0)
        {
            throw new ArgumentException("At least one value is required.", paramName);
        }

        return normalized;
    }

    internal static IReadOnlyList<string> NormalizeOptionalList(IReadOnlyList<string>? values) =>
        (values ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}

public record ElectionAdminOnlyProtectedTallyCustodyRestrictedEvidence(
    string PrivateCustodyRowReference,
    string? KmsKeyId,
    string? KmsKeyArn,
    string? KmsAlias,
    string? KmsRegion,
    string? KmsAccountBoundary,
    string? KmsRawTagSet,
    string? IamRoleReference,
    string? SealedEnvelopeHash,
    string? ProviderErrorCode,
    string? ProviderErrorMessage)
{
    public string PrivateCustodyRowReference { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyPublicEvidence.NormalizeRequired(
            PrivateCustodyRowReference,
            nameof(PrivateCustodyRowReference));
    public string? KmsKeyId { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyPublicEvidence.NormalizeOptional(KmsKeyId);
    public string? KmsKeyArn { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyPublicEvidence.NormalizeOptional(KmsKeyArn);
    public string? KmsAlias { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyPublicEvidence.NormalizeOptional(KmsAlias);
    public string? KmsRegion { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyPublicEvidence.NormalizeOptional(KmsRegion);
    public string? KmsAccountBoundary { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyPublicEvidence.NormalizeOptional(KmsAccountBoundary);
    public string? KmsRawTagSet { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyPublicEvidence.NormalizeOptional(KmsRawTagSet);
    public string? IamRoleReference { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyPublicEvidence.NormalizeOptional(IamRoleReference);
    public string? SealedEnvelopeHash { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyPublicEvidence.NormalizeOptional(SealedEnvelopeHash);
    public string? ProviderErrorCode { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyPublicEvidence.NormalizeOptional(ProviderErrorCode);
    public string? ProviderErrorMessage { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyPublicEvidence.NormalizeOptional(ProviderErrorMessage);
}

public record ElectionAdminOnlyProtectedTallyCustodyExceptionEvidence(
    string ExceptionId,
    ElectionAdminOnlyProtectedTallyCustodyActionKind ActionKind,
    ElectionAdminOnlyProtectedTallyCustodyActionResult ActionResult,
    string ReasonCode,
    string PublicImpact,
    string? RestrictedOperatorNotes,
    bool BlocksReadinessScoreIncrease,
    DateTime RecordedAt)
{
    public string ExceptionId { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyPublicEvidence.NormalizeRequired(ExceptionId, nameof(ExceptionId));
    public string ReasonCode { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyPublicEvidence.NormalizeRequired(ReasonCode, nameof(ReasonCode));
    public string PublicImpact { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyPublicEvidence.NormalizeRequired(PublicImpact, nameof(PublicImpact));
    public string? RestrictedOperatorNotes { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyPublicEvidence.NormalizeOptional(RestrictedOperatorNotes);
}

public record ElectionAdminOnlyProtectedTallyCustodyReadinessFragment(
    ElectionAdminOnlyProtectedTallyCustodyPublicEvidence PublicEvidence,
    ElectionAdminOnlyProtectedTallyCustodyRestrictedEvidence? RestrictedEvidence,
    IReadOnlyList<ElectionAdminOnlyProtectedTallyCustodyExceptionEvidence> Exceptions,
    IReadOnlyList<string> AcceptedGateIds,
    IReadOnlyList<string> ResidualRiskIds,
    int? ProposedScore)
{
    public string EvidenceId => ElectionAdminOnlyProtectedTallyCustodyReadinessIds.EvidenceId;
    public string DimensionId => ElectionAdminOnlyProtectedTallyCustodyReadinessIds.DimensionId;
    public string BlockerId => ElectionAdminOnlyProtectedTallyCustodyReadinessIds.PilotBlockerId;

    public IReadOnlyList<ElectionAdminOnlyProtectedTallyCustodyExceptionEvidence> Exceptions { get; init; } =
        Exceptions?.ToArray() ?? Array.Empty<ElectionAdminOnlyProtectedTallyCustodyExceptionEvidence>();

    public IReadOnlyList<string> AcceptedGateIds { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyPublicEvidence.NormalizeOptionalList(AcceptedGateIds);

    public IReadOnlyList<string> ResidualRiskIds { get; init; } =
        ElectionAdminOnlyProtectedTallyCustodyPublicEvidence.NormalizeOptionalList(ResidualRiskIds);

    public int? ProposedScore { get; init; } = ProposedScore is null ? null : Math.Clamp(ProposedScore.Value, 0, 10);

    public bool HasAcceptedAllRequiredGates =>
        ElectionAdminOnlyProtectedTallyCustodyReadinessIds.RequiredGateIds
            .All(required => AcceptedGateIds.Contains(required, StringComparer.Ordinal));

    public bool CanProposeTargetScoreIncrease =>
        HasAcceptedAllRequiredGates &&
        ProposedScore is >= 8 &&
        Exceptions.All(x => !x.BlocksReadinessScoreIncrease);
}
