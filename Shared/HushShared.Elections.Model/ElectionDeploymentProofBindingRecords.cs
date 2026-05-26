namespace HushShared.Elections.Model;

public static class ElectionDeploymentProofConstants
{
    public const string SchemaVersion = "hushvoting-deployment-proof-binding-v1";
    public const string DeploymentProtocolVersion = "hushvoting-deployment-protocol-v1";
    public const string PublicLedgerArtifactSchemaId = "hushvoting-deployment-proof-public-ledger-v1";
    public const string PublicLedgerArtifactFileName = "deployment-proof-binding-ledger.json";
    public const string RetentionLogPrivacyProofFamilyId = "retention_log_privacy_no_durable_join";
    public const string Feat137SourceFeature = "FEAT-137";
    public const string Feat144WebClientProofNotSupportedCode = "webclient_proof_not_yet_supported";
    public const string Feat144WebClientProofMismatchCode = "webclient_proof_mismatch";
    public const string Feat144WebClientExpectedProofMissingCode = "webclient_expected_proof_missing";
    public const string PrivacyProofMissingCode = "privacy_proof_missing";
    public const string PrivacyProofStaleCode = "privacy_proof_stale";
    public const string PrivacyProofPrivateOnlyCode = "privacy_proof_private_only";
    public const string PrivacyProofMismatchCode = "privacy_proof_mismatch";
    public const string PrivacyProofUnknownCode = "privacy_proof_unknown";
}

public record ElectionDeploymentProofLedgerRecord(
    Guid Id,
    ElectionId ElectionId,
    string LedgerPublicId,
    string SchemaVersion,
    ElectionDeploymentProofEvidenceStatus Status,
    ElectionDeploymentProofLedgerVisibility Visibility,
    string DeploymentProfile,
    string DeploymentProtocolVersion,
    string? PublicCatalogRepository,
    string? PublicCatalogRef,
    string? PublicCatalogCommit,
    string? PlatformCeremonyId,
    string? ActiveProofSetIdAtOpen,
    DateTime? OpenedAtUtc,
    DateTime? ClosedAtUtc,
    DateTime? FinalizedAtUtc,
    DateTime? VoidedAtUtc,
    Guid? LatestCheckpointId,
    ElectionDeploymentProofEvidenceStatus? FinalStatus,
    string? PublicLedgerArtifactRef,
    string? PublicLedgerArtifactHash,
    string? RestrictedEvidenceIndexRef,
    DateTime CreatedAtUtc,
    DateTime LastReconciledAtUtc)
{
    public Guid Id { get; init; } =
        DeploymentProofBindingRecordValidation.RequireGuid(Id, nameof(Id));

    public string LedgerPublicId { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeRequiredPublicSafeValue(
            LedgerPublicId,
            nameof(LedgerPublicId));

    public string SchemaVersion { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeRequiredPublicSafeValue(
            SchemaVersion,
            nameof(SchemaVersion));

    public string DeploymentProfile { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeRequiredPublicSafeValue(
            DeploymentProfile,
            nameof(DeploymentProfile));

    public string DeploymentProtocolVersion { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeRequiredPublicSafeValue(
            DeploymentProtocolVersion,
            nameof(DeploymentProtocolVersion));

    public string? PublicCatalogRepository { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(PublicCatalogRepository);

    public string? PublicCatalogRef { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(PublicCatalogRef);

    public string? PublicCatalogCommit { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(PublicCatalogCommit);

    public string? PlatformCeremonyId { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(PlatformCeremonyId);

    public string? ActiveProofSetIdAtOpen { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(ActiveProofSetIdAtOpen);

    public Guid? LatestCheckpointId { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalGuid(LatestCheckpointId);

    public string? PublicLedgerArtifactRef { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(PublicLedgerArtifactRef);

    public string? PublicLedgerArtifactHash { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalSha256Hash(
            PublicLedgerArtifactHash,
            nameof(PublicLedgerArtifactHash));

    public string? RestrictedEvidenceIndexRef { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(RestrictedEvidenceIndexRef);

    public bool BlocksDeploymentProofClaims =>
        Status.BlocksDeploymentProofClaims() ||
        FinalStatus.HasValue && FinalStatus.Value.BlocksDeploymentProofClaims();
}

public record ElectionDeploymentProofCheckpointRecord(
    Guid Id,
    Guid LedgerId,
    ElectionId ElectionId,
    ElectionDeploymentProofCheckpointType CheckpointType,
    ElectionLifecycleState SourceLifecycleState,
    ElectionLifecycleState TargetLifecycleState,
    Guid? TransitionArtifactId,
    Guid? ReportPackageId,
    string? ProofSetId,
    ElectionDeploymentProofEvidenceStatus EvidenceStatus,
    ElectionDeploymentProofClaimEffect ClaimEffect,
    DateTime ObservedAtUtc,
    ElectionDeploymentProofEvidenceStatus ProviderStatus,
    IReadOnlyList<string> ProviderErrorCodes,
    Guid? SupersedesCheckpointId,
    string PublicSummary,
    Guid? SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId)
{
    public Guid Id { get; init; } =
        DeploymentProofBindingRecordValidation.RequireGuid(Id, nameof(Id));

    public Guid LedgerId { get; init; } =
        DeploymentProofBindingRecordValidation.RequireGuid(LedgerId, nameof(LedgerId));

    public Guid? TransitionArtifactId { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalGuid(TransitionArtifactId);

    public Guid? ReportPackageId { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalGuid(ReportPackageId);

    public string? ProofSetId { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(ProofSetId);

    public IReadOnlyList<string> ProviderErrorCodes { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizePublicSafeStringList(ProviderErrorCodes);

    public Guid? SupersedesCheckpointId { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalGuid(SupersedesCheckpointId);

    public string PublicSummary { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeRequiredPublicSafeValue(
            PublicSummary,
            nameof(PublicSummary));

    public bool BlocksDeploymentProofClaims =>
        EvidenceStatus.BlocksDeploymentProofClaims() ||
        ProviderStatus.BlocksDeploymentProofClaims() ||
        ClaimEffect == ElectionDeploymentProofClaimEffect.Blocked;
}

public record ElectionDeploymentProofComponentObservationRecord(
    Guid Id,
    Guid CheckpointId,
    ElectionId ElectionId,
    ElectionDeploymentProofComponentId ComponentId,
    string? DeploymentProofId,
    string? ExpectedDeploymentProofId,
    string? ObservedDeploymentProofId,
    string? ExpectedArtifactHash,
    string? ObservedArtifactHash,
    ElectionDeploymentProofEvidenceStatus EvidenceStatus,
    ElectionDeploymentProofObservationSource ObservationSource,
    string? SourceRef,
    string? ArtifactHash,
    string? PackageHash,
    string? PublicPackageRef,
    string? MismatchCode,
    IReadOnlyList<string> SupersedesProofIds,
    DateTime ObservedAtUtc)
{
    public Guid Id { get; init; } =
        DeploymentProofBindingRecordValidation.RequireGuid(Id, nameof(Id));

    public Guid CheckpointId { get; init; } =
        DeploymentProofBindingRecordValidation.RequireGuid(CheckpointId, nameof(CheckpointId));

    public string? DeploymentProofId { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(DeploymentProofId);

    public string? ExpectedDeploymentProofId { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(ExpectedDeploymentProofId);

    public string? ObservedDeploymentProofId { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(ObservedDeploymentProofId);

    public string? ExpectedArtifactHash { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(ExpectedArtifactHash);

    public string? ObservedArtifactHash { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(ObservedArtifactHash);

    public string? SourceRef { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(SourceRef);

    public string? ArtifactHash { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(ArtifactHash);

    public string? PackageHash { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalSha256Hash(PackageHash, nameof(PackageHash));

    public string? PublicPackageRef { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(PublicPackageRef);

    public string? MismatchCode { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(MismatchCode);

    public IReadOnlyList<string> SupersedesProofIds { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizePublicSafeStringList(SupersedesProofIds);
}

public record ElectionDeploymentProofEventRecord(
    Guid Id,
    Guid CheckpointId,
    ElectionId ElectionId,
    string EventPublicId,
    string EventType,
    string? DeploymentRunId,
    ElectionDeploymentProofComponentId ComponentId,
    string? BeforeProofId,
    string? AfterProofId,
    ElectionDeploymentProofImpactClassification Classification,
    string? Reason,
    IReadOnlyList<string> ChecksRerun,
    string? CheckResult,
    string? AccountabilityMarker,
    DateTime OccurredAtUtc,
    ElectionDeploymentProofEvidenceStatus EvidenceStatus)
{
    public Guid Id { get; init; } =
        DeploymentProofBindingRecordValidation.RequireGuid(Id, nameof(Id));

    public Guid CheckpointId { get; init; } =
        DeploymentProofBindingRecordValidation.RequireGuid(CheckpointId, nameof(CheckpointId));

    public string EventPublicId { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeRequiredPublicSafeValue(
            EventPublicId,
            nameof(EventPublicId));

    public string EventType { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeRequiredPublicSafeValue(
            EventType,
            nameof(EventType));

    public string? DeploymentRunId { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(DeploymentRunId);

    public string? BeforeProofId { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(BeforeProofId);

    public string? AfterProofId { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(AfterProofId);

    public string? Reason { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(Reason);

    public IReadOnlyList<string> ChecksRerun { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizePublicSafeStringList(ChecksRerun);

    public string? CheckResult { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(CheckResult);

    public string? AccountabilityMarker { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(AccountabilityMarker);

    public bool RequiresClassificationRemediation =>
        Classification == ElectionDeploymentProofImpactClassification.UnknownPendingClassification ||
        EvidenceStatus == ElectionDeploymentProofEvidenceStatus.Unknown;
}

public record ElectionProofFamilyBindingStatusRecord(
    Guid Id,
    Guid CheckpointId,
    ElectionId ElectionId,
    string ProofFamilyId,
    string ProofFamilyVersion,
    string? PackageId,
    string? PackageHash,
    string? PromotedRegisterRef,
    string SourceFeature,
    ElectionDeploymentProofEvidenceStatus EvidenceStatus,
    string? MismatchCode,
    string PublicSummary,
    DateTime ObservedAtUtc)
{
    public Guid Id { get; init; } =
        DeploymentProofBindingRecordValidation.RequireGuid(Id, nameof(Id));

    public Guid CheckpointId { get; init; } =
        DeploymentProofBindingRecordValidation.RequireGuid(CheckpointId, nameof(CheckpointId));

    public string ProofFamilyId { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeRequiredPublicSafeValue(
            ProofFamilyId,
            nameof(ProofFamilyId));

    public string ProofFamilyVersion { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeRequiredPublicSafeValue(
            ProofFamilyVersion,
            nameof(ProofFamilyVersion));

    public string? PackageId { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(PackageId);

    public string? PackageHash { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalSha256Hash(PackageHash, nameof(PackageHash));

    public string? PromotedRegisterRef { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(PromotedRegisterRef);

    public string SourceFeature { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeRequiredPublicSafeValue(
            SourceFeature,
            nameof(SourceFeature));

    public string? MismatchCode { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeOptionalPublicSafeValue(MismatchCode);

    public string PublicSummary { get; init; } =
        DeploymentProofBindingRecordValidation.NormalizeRequiredPublicSafeValue(
            PublicSummary,
            nameof(PublicSummary));
}

public record ElectionDeploymentProofPublicLedgerArtifactRecord(
    string SchemaId,
    string ElectionId,
    Guid LedgerId,
    string LedgerPublicId,
    string Status,
    string FinalStatus,
    string ClaimEffect,
    bool BlocksDeploymentProofClaims,
    string ClaimSummary,
    string DeploymentProfile,
    string DeploymentProtocolVersion,
    string? PublicCatalogRepository,
    string? PublicCatalogRef,
    string? PublicCatalogCommit,
    string? PlatformCeremonyId,
    string? ActiveProofSetIdAtOpen,
    DateTime? OpenedAtUtc,
    DateTime? ClosedAtUtc,
    DateTime? FinalizedAtUtc,
    DateTime? VoidedAtUtc,
    Guid? LatestCheckpointId,
    DateTime CreatedAtUtc,
    DateTime LastReconciledAtUtc,
    IReadOnlyList<string> ClaimLimitations,
    IReadOnlyList<ElectionDeploymentProofPublicCheckpointArtifactRecord> Checkpoints,
    IReadOnlyList<ElectionDeploymentProofPublicComponentObservationArtifactRecord> ComponentObservations,
    IReadOnlyList<ElectionDeploymentProofPublicEventArtifactRecord> DeploymentEvents,
    IReadOnlyList<ElectionDeploymentProofPublicProofFamilyArtifactRecord> ProofFamilies,
    IReadOnlyList<string> PublicPrivacyBoundary);

public record ElectionDeploymentProofPublicCheckpointArtifactRecord(
    Guid CheckpointId,
    string CheckpointType,
    string SourceLifecycleState,
    string TargetLifecycleState,
    Guid? TransitionArtifactId,
    Guid? ReportPackageId,
    string? ProofSetId,
    string EvidenceStatus,
    string ProviderStatus,
    string ClaimEffect,
    IReadOnlyList<string> ProviderErrorCodes,
    Guid? SupersedesCheckpointId,
    string PublicSummary,
    Guid? SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId,
    DateTime ObservedAtUtc);

public record ElectionDeploymentProofPublicComponentObservationArtifactRecord(
    Guid ObservationId,
    Guid CheckpointId,
    string ComponentId,
    string? DeploymentProofId,
    string? ExpectedDeploymentProofId,
    string? ObservedDeploymentProofId,
    string? ExpectedArtifactHash,
    string? ObservedArtifactHash,
    string EvidenceStatus,
    string ObservationSource,
    string? SourceRef,
    string? ArtifactHash,
    string? PackageHash,
    string? PublicPackageRef,
    string? MismatchCode,
    IReadOnlyList<string> SupersedesProofIds,
    DateTime ObservedAtUtc);

public record ElectionDeploymentProofPublicEventArtifactRecord(
    Guid EventId,
    Guid CheckpointId,
    string EventPublicId,
    string EventType,
    string? DeploymentRunId,
    string ComponentId,
    string? BeforeProofId,
    string? AfterProofId,
    string Classification,
    string? Reason,
    IReadOnlyList<string> ChecksRerun,
    string? CheckResult,
    string? AccountabilityMarker,
    DateTime OccurredAtUtc,
    string EvidenceStatus);

public record ElectionDeploymentProofPublicProofFamilyArtifactRecord(
    Guid ProofFamilyBindingStatusId,
    Guid CheckpointId,
    string ProofFamilyId,
    string ProofFamilyVersion,
    string? PackageId,
    string? PackageHash,
    string? PromotedRegisterRef,
    string SourceFeature,
    string EvidenceStatus,
    string ClaimEffect,
    string? MismatchCode,
    string PublicSummary,
    DateTime ObservedAtUtc);

public static class ElectionDeploymentProofStatusExtensions
{
    public static bool BlocksDeploymentProofClaims(this ElectionDeploymentProofEvidenceStatus status) =>
        status is
            ElectionDeploymentProofEvidenceStatus.Blocked or
            ElectionDeploymentProofEvidenceStatus.Missing or
            ElectionDeploymentProofEvidenceStatus.Stale or
            ElectionDeploymentProofEvidenceStatus.Superseded or
            ElectionDeploymentProofEvidenceStatus.Unknown or
            ElectionDeploymentProofEvidenceStatus.Mismatch or
            ElectionDeploymentProofEvidenceStatus.NotYetSupported;
}

internal static class DeploymentProofBindingRecordValidation
{
    private static readonly string[] ForbiddenPublicFragments =
    [
        "private key",
        "begin private key",
        "kms:",
        "aws:kms",
        "kms alias",
        "password",
        "secret access key",
        "connection string",
        "raw log",
        "support log",
        "voter identity",
        "vote choice",
        "trustee share",
        "receipt secret",
    ];

    public static Guid RequireGuid(Guid value, string paramName) =>
        value == Guid.Empty
            ? throw new ArgumentException("Value is required.", paramName)
            : value;

    public static Guid? NormalizeOptionalGuid(Guid? value) =>
        value == Guid.Empty ? null : value;

    public static string NormalizeRequiredPublicSafeValue(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return EnsurePublicSafe(value.Trim(), paramName);
    }

    public static string? NormalizeOptionalPublicSafeValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : EnsurePublicSafe(value.Trim(), nameof(value));

    public static string? NormalizeOptionalSha256Hash(string? value, string paramName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : ProtocolPackageRecordValidation.NormalizeSha256Hash(value, paramName);

    public static IReadOnlyList<string> NormalizePublicSafeStringList(IReadOnlyList<string>? values) =>
        values is null
            ? Array.Empty<string>()
            : values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => NormalizeRequiredPublicSafeValue(x, nameof(values)))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

    private static string EnsurePublicSafe(string value, string paramName)
    {
        if (ForbiddenPublicFragments.Any(fragment =>
                value.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Public deployment proof values cannot contain restricted material.", paramName);
        }

        return value;
    }
}
