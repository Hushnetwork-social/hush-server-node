namespace HushShared.Elections.Verification.Model;

public static class ElectionSp10ProfileIds
{
    public const string OperationalSecuritySummarySchema = "HushVotingOperationalSecuritySummary-v1";
    public const string OperationalDeploymentEvidenceSchema = "HushVotingOperationalDeploymentEvidence-v1";
    public const string OperationalCustodyEvidenceSchema = "HushVotingOperationalCustodyEvidence-v1";
    public const string OperationalVerifierOutputSchema = "HushVotingOperationalVerifierOutput-v1";
    public const string RestrictedAccessControlSnapshotSchema = "HushVotingOperationalAccessControlSnapshot-v1";
    public const string RestrictedLoggingEvidenceSchema = "HushVotingOperationalLoggingEvidence-v1";
    public const string RestrictedBackupRestoreEvidenceSchema = "HushVotingOperationalBackupRestoreEvidence-v1";
    public const string RestrictedIncidentEvidenceSchema = "HushVotingOperationalIncidentEvidence-v1";
    public const string RestrictedAuditorRoomAccessLogSchema = "HushVotingAuditorRoomAccessLog-v1";
    public const string OperationalSecurityProgramVersion = "SP10-P1";

    public const string DeploymentProfileManagedAwsContainerV1 = "hushvoting_managed_aws_container_v1";
    public const string CustodyModeAwsKmsPerElectionEnvelopeV1 = "aws_kms_per_election_envelope_v1";
    public const string CustodyModeTrusteeLocalSecureVaultV1 = "trustee_local_secure_vault_v1";
    public const string ExecutorKeyLifecycleEphemeralMemoryV1 = "executor_ephemeral_memory_key_v1";
    public const string IncidentStatusNoIncidentDeclared = "no_incident_declared";
    public const string IncidentStatusIncidentDeclared = "incident_declared";

    public const string EvidenceStateNotAvailable = "not_available";
    public const string EvidenceStateDevelopmentPlaceholder = "development_placeholder";
    public const string EvidenceStateManagedProfileDeclared = "managed_profile_declared";
    public const string EvidenceStateManagedProfileEvidenceAvailable = "managed_profile_evidence_available";
    public const string EvidenceStateManagedProfileExceptionDeclared = "managed_profile_exception_declared";
    public const string EvidenceStateBlocked = "blocked";

    public const string DeploymentProfileDeclaredCheckCode = "OPS-000";
    public const string ReleaseDeploymentBindingCheckCode = "OPS-001";
    public const string AccessControlSnapshotCheckCode = "OPS-002";
    public const string CustodyModeDeclaredCheckCode = "OPS-003";
    public const string ExecutorKeyLifecycleCheckCode = "OPS-004";
    public const string ForbiddenMaterialScanCheckCode = "OPS-005";
    public const string BackupRestoreEvidenceCheckCode = "OPS-006";
    public const string IncidentDeclarationCheckCode = "OPS-007";
    public const string AuditorRoomAccessLogCheckCode = "OPS-008";

    public static IReadOnlyList<string> OperationalEvidenceStates { get; } =
    [
        EvidenceStateNotAvailable,
        EvidenceStateDevelopmentPlaceholder,
        EvidenceStateManagedProfileDeclared,
        EvidenceStateManagedProfileEvidenceAvailable,
        EvidenceStateManagedProfileExceptionDeclared,
        EvidenceStateBlocked,
    ];

    public static IReadOnlySet<string> OperationalEvidenceStateSet { get; } = new HashSet<string>(
        OperationalEvidenceStates,
        StringComparer.Ordinal);

    public static IReadOnlyList<string> CustodyModes { get; } =
    [
        CustodyModeAwsKmsPerElectionEnvelopeV1,
        CustodyModeTrusteeLocalSecureVaultV1,
    ];

    public static IReadOnlySet<string> CustodyModeSet { get; } = new HashSet<string>(
        CustodyModes,
        StringComparer.Ordinal);

    public static IReadOnlyList<string> IncidentStatuses { get; } =
    [
        IncidentStatusNoIncidentDeclared,
        IncidentStatusIncidentDeclared,
    ];

    public static IReadOnlySet<string> IncidentStatusSet { get; } = new HashSet<string>(
        IncidentStatuses,
        StringComparer.Ordinal);

    public static IReadOnlyList<string> OperationalCheckCodes { get; } =
    [
        DeploymentProfileDeclaredCheckCode,
        ReleaseDeploymentBindingCheckCode,
        AccessControlSnapshotCheckCode,
        CustodyModeDeclaredCheckCode,
        ExecutorKeyLifecycleCheckCode,
        ForbiddenMaterialScanCheckCode,
        BackupRestoreEvidenceCheckCode,
        IncidentDeclarationCheckCode,
        AuditorRoomAccessLogCheckCode,
    ];

    public static IReadOnlyDictionary<string, ElectionSp10OperationalCheckDefinitionRecord> OperationalCheckDefinitions { get; } =
        new Dictionary<string, ElectionSp10OperationalCheckDefinitionRecord>(StringComparer.Ordinal)
        {
            [DeploymentProfileDeclaredCheckCode] = new(
                DeploymentProfileDeclaredCheckCode,
                VerificationResultCodes.OperationalSecurityProfileDeclared,
                VerificationCheckStatus.Pass,
                "Managed deployment profile is declared without implying FEAT-106 readiness."),
            [ReleaseDeploymentBindingCheckCode] = new(
                ReleaseDeploymentBindingCheckCode,
                VerificationResultCodes.OperationalSecurityReleaseBindingMissing,
                VerificationCheckStatus.Fail,
                "Deployment evidence must bind to SP-08 release manifest hashes and immutable image digests."),
            [AccessControlSnapshotCheckCode] = new(
                AccessControlSnapshotCheckCode,
                VerificationResultCodes.OperationalSecurityAccessSnapshotMissing,
                VerificationCheckStatus.Fail,
                "Restricted package must carry an access-control snapshot hash or reference."),
            [CustodyModeDeclaredCheckCode] = new(
                CustodyModeDeclaredCheckCode,
                VerificationResultCodes.OperationalSecurityCustodyModeMissing,
                VerificationCheckStatus.Fail,
                "Custody evidence must declare the governance-mode-specific custody model."),
            [ExecutorKeyLifecycleCheckCode] = new(
                ExecutorKeyLifecycleCheckCode,
                VerificationResultCodes.OperationalSecurityExecutorKeyLifecycleMissing,
                VerificationCheckStatus.Fail,
                "Executor key lifecycle evidence must show ephemeral in-memory key handling and destruction."),
            [ForbiddenMaterialScanCheckCode] = new(
                ForbiddenMaterialScanCheckCode,
                VerificationResultCodes.OperationalSecurityForbiddenMaterial,
                VerificationCheckStatus.Fail,
                "Public and restricted package scans must reject forbidden operational or voting material."),
            [BackupRestoreEvidenceCheckCode] = new(
                BackupRestoreEvidenceCheckCode,
                VerificationResultCodes.OperationalSecurityBackupRestoreMissing,
                VerificationCheckStatus.Fail,
                "Backup/restore evidence must be exported or referenced for high-assurance operational claims."),
            [IncidentDeclarationCheckCode] = new(
                IncidentDeclarationCheckCode,
                VerificationResultCodes.OperationalSecurityIncidentDeclarationMissing,
                VerificationCheckStatus.Fail,
                "Package must declare no incident or incident-declared status."),
            [AuditorRoomAccessLogCheckCode] = new(
                AuditorRoomAccessLogCheckCode,
                VerificationResultCodes.OperationalSecurityAuditorRoomMissing,
                VerificationCheckStatus.Fail,
                "Restricted auditor-room access-log hash or reference must exist when restricted evidence is available."),
        };

    public static IReadOnlyDictionary<string, string> AllowedWordingByEvidenceState { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EvidenceStateNotAvailable] =
                "Operational security evidence is not available for this package; FEAT-106 readiness is not completed.",
            [EvidenceStateDevelopmentPlaceholder] =
                "Development-only operational placeholders are present and cannot support high-assurance operational claims.",
            [EvidenceStateManagedProfileDeclared] =
                "Managed deployment profile is declared; supporting operational evidence is not yet complete.",
            [EvidenceStateManagedProfileEvidenceAvailable] =
                "Managed deployment profile evidence is available for the declared scope; this is not legal approval or certification.",
            [EvidenceStateManagedProfileExceptionDeclared] =
                "Managed deployment profile exception is declared and limits operational assurance for this package.",
            [EvidenceStateBlocked] =
                "Operational security evidence is blocked for the declared scope; high-assurance operational claims are not allowed.",
        };

    public static IReadOnlyList<string> ForbiddenClaimPhrases { get; } =
    [
        "feat-106 complete",
        "rollout readiness approved",
        "certified",
        "legal approval",
        "approved for public elections",
        "same assurance as swiss public e-voting",
        "external infrastructure audit complete",
    ];
}

public static class ElectionSp11ProfileIds
{
    public const string RegulatoryClaimStateSchema = "HushVotingRegulatoryClaimState-v1";
    public const string RegulatoryTrackerVersion = "SP11-P1";

    public const string ClaimStateAllowedNow = "allowed_now";
    public const string ClaimStateAllowedWithLimitation = "allowed_with_limitation";
    public const string ClaimStateBlockedUntilReview = "blocked_until_review";
    public const string ClaimStateBlockedUntilCertification = "blocked_until_certification";
    public const string ClaimStateForbidden = "forbidden";

    public const string RegulatoryClaimShapeValidCheckCode = "REG-000";
    public const string ClaimAllowedByRegisterCheckCode = "REG-001";
    public const string BlockedCertificationClaimCheckCode = "REG-002";
    public const string StaleTrackerWarningCheckCode = "REG-003";
    public const string RestrictedWorkpaperBoundaryCheckCode = "REG-004";

    public static IReadOnlyList<string> ClaimStates { get; } =
    [
        ClaimStateAllowedNow,
        ClaimStateAllowedWithLimitation,
        ClaimStateBlockedUntilReview,
        ClaimStateBlockedUntilCertification,
        ClaimStateForbidden,
    ];

    public static IReadOnlySet<string> ClaimStateSet { get; } = new HashSet<string>(
        ClaimStates,
        StringComparer.Ordinal);

    public static IReadOnlyList<string> RegulatoryCheckCodes { get; } =
    [
        RegulatoryClaimShapeValidCheckCode,
        ClaimAllowedByRegisterCheckCode,
        BlockedCertificationClaimCheckCode,
        StaleTrackerWarningCheckCode,
        RestrictedWorkpaperBoundaryCheckCode,
    ];

    public static IReadOnlyDictionary<string, ElectionSp11RegulatoryCheckDefinitionRecord> RegulatoryCheckDefinitions { get; } =
        new Dictionary<string, ElectionSp11RegulatoryCheckDefinitionRecord>(StringComparer.Ordinal)
        {
            [RegulatoryClaimShapeValidCheckCode] = new(
                RegulatoryClaimShapeValidCheckCode,
                VerificationResultCodes.RegulatoryClaimShapeValid,
                VerificationCheckStatus.Pass,
                "Regulatory claim artifact has the expected shape and non-legal-advice boundary."),
            [ClaimAllowedByRegisterCheckCode] = new(
                ClaimAllowedByRegisterCheckCode,
                VerificationResultCodes.RegulatoryClaimAllowedByRegister,
                VerificationCheckStatus.Pass,
                "Claim is currently allowed by the register for the declared organizational-election scope."),
            [BlockedCertificationClaimCheckCode] = new(
                BlockedCertificationClaimCheckCode,
                VerificationResultCodes.RegulatoryClaimBlockedCertification,
                VerificationCheckStatus.Fail,
                "Certification, authority approval, or public-election parity claims remain blocked without authority evidence."),
            [StaleTrackerWarningCheckCode] = new(
                StaleTrackerWarningCheckCode,
                VerificationResultCodes.RegulatoryTrackerStale,
                VerificationCheckStatus.Warn,
                "Tracker source or next-review date is stale and must not support new claims."),
            [RestrictedWorkpaperBoundaryCheckCode] = new(
                RestrictedWorkpaperBoundaryCheckCode,
                VerificationResultCodes.RegulatoryRestrictedWorkpaperBoundary,
                VerificationCheckStatus.Fail,
                "Restricted jurisdiction workpapers must not appear in public package artifacts."),
        };

    public static IReadOnlyDictionary<string, string> AllowedWordingByClaimState { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ClaimStateAllowedNow] =
                "Regulatory tracker currently allows this organizational-election claim for the declared scope; this is not legal advice.",
            [ClaimStateAllowedWithLimitation] =
                "Regulatory tracker allows this claim only with the listed limitations; this is not legal advice.",
            [ClaimStateBlockedUntilReview] =
                "Regulatory tracker blocks this claim until business/legal review updates the register.",
            [ClaimStateBlockedUntilCertification] =
                "Regulatory tracker blocks this claim until required authority or certification evidence exists.",
            [ClaimStateForbidden] =
                "Regulatory tracker forbids this claim for the declared scope.",
        };

    public static IReadOnlyList<string> ForbiddenClaimPhrases { get; } =
    [
        "legal advice",
        "legally approved",
        "certified",
        "certification complete",
        "approved for swiss public elections",
        "approved for public elections",
        "authority approved",
    ];
}

public record ElectionSp10OperationalCheckDefinitionRecord(
    string CheckCode,
    string ResultCode,
    VerificationCheckStatus ViolationStatus,
    string Description);

public record ElectionSp11RegulatoryCheckDefinitionRecord(
    string CheckCode,
    string ResultCode,
    VerificationCheckStatus ViolationStatus,
    string Description);

public record ElectionSp10OperationalSecurityStatusArtifactRecord(
    string Schema,
    string ElectionId,
    string ProgramVersion,
    string DeploymentProfileId,
    string EvidenceState,
    bool DoesNotCompleteFeat106Readiness,
    string Feat106ReadinessCaveat,
    string? ReleaseEvidenceMode,
    string? ReleaseManifestHash,
    string? ImmutableDeploymentRef,
    string? CustodyMode,
    string? ExecutorKeyLifecycle,
    string? AccessSnapshotHashOrRestrictedRef,
    string? BackupRestoreHashOrRestrictedRef,
    string? IncidentStatus,
    string? AuditorRoomAccessLogHashOrRestrictedRef,
    bool BlocksHighAssurance,
    string PrimaryResultCode,
    string? PrimaryIssue,
    IReadOnlyList<string> PublicEvidenceFiles,
    IReadOnlyList<string> RestrictedEvidenceFiles,
    IReadOnlyList<string> PublicPrivacyBoundary)
{
    public string Schema { get; init; } = NormalizeRequiredValue(Schema, nameof(Schema));
    public string ElectionId { get; init; } = NormalizeRequiredValue(ElectionId, nameof(ElectionId));
    public string ProgramVersion { get; init; } = NormalizeRequiredValue(ProgramVersion, nameof(ProgramVersion));
    public string DeploymentProfileId { get; init; } =
        NormalizeRequiredValue(DeploymentProfileId, nameof(DeploymentProfileId));
    public string EvidenceState { get; init; } = NormalizeRequiredValue(EvidenceState, nameof(EvidenceState));
    public string Feat106ReadinessCaveat { get; init; } =
        NormalizeRequiredValue(Feat106ReadinessCaveat, nameof(Feat106ReadinessCaveat));
    public string? ReleaseEvidenceMode { get; init; } = NormalizeOptionalValue(ReleaseEvidenceMode);
    public string? ReleaseManifestHash { get; init; } = NormalizeOptionalValue(ReleaseManifestHash);
    public string? ImmutableDeploymentRef { get; init; } = NormalizeOptionalValue(ImmutableDeploymentRef);
    public string? CustodyMode { get; init; } = NormalizeOptionalValue(CustodyMode);
    public string? ExecutorKeyLifecycle { get; init; } = NormalizeOptionalValue(ExecutorKeyLifecycle);
    public string? AccessSnapshotHashOrRestrictedRef { get; init; } =
        NormalizeOptionalValue(AccessSnapshotHashOrRestrictedRef);
    public string? BackupRestoreHashOrRestrictedRef { get; init; } =
        NormalizeOptionalValue(BackupRestoreHashOrRestrictedRef);
    public string? IncidentStatus { get; init; } = NormalizeOptionalValue(IncidentStatus);
    public string? AuditorRoomAccessLogHashOrRestrictedRef { get; init; } =
        NormalizeOptionalValue(AuditorRoomAccessLogHashOrRestrictedRef);
    public string PrimaryResultCode { get; init; } =
        NormalizeRequiredValue(PrimaryResultCode, nameof(PrimaryResultCode));
    public string? PrimaryIssue { get; init; } = NormalizeOptionalValue(PrimaryIssue);
    public IReadOnlyList<string> PublicEvidenceFiles { get; init; } = NormalizeStringList(PublicEvidenceFiles);
    public IReadOnlyList<string> RestrictedEvidenceFiles { get; init; } = NormalizeStringList(RestrictedEvidenceFiles);
    public IReadOnlyList<string> PublicPrivacyBoundary { get; init; } = NormalizeStringList(PublicPrivacyBoundary);

    internal static string NormalizeRequiredValue(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.Trim();
    }

    internal static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static IReadOnlyList<string> NormalizeStringList(IReadOnlyList<string>? values) =>
        values is null
            ? Array.Empty<string>()
            : values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
}

public record ElectionSp10OperationalDeploymentEvidenceArtifactRecord(
    string Schema,
    string ElectionId,
    string ProgramVersion,
    string DeploymentProfileId,
    string EvidenceState,
    string ReleaseEvidenceMode,
    string? ReleaseManifestHash,
    string? ImmutableDeploymentRef,
    string SourceAuthority,
    IReadOnlyList<string> PublicEvidenceFiles,
    IReadOnlyList<string> PublicPrivacyBoundary)
{
    public string Schema { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(Schema, nameof(Schema));
    public string ElectionId { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(ElectionId, nameof(ElectionId));
    public string ProgramVersion { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(
            ProgramVersion,
            nameof(ProgramVersion));
    public string DeploymentProfileId { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(
            DeploymentProfileId,
            nameof(DeploymentProfileId));
    public string EvidenceState { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(
            EvidenceState,
            nameof(EvidenceState));
    public string ReleaseEvidenceMode { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(
            ReleaseEvidenceMode,
            nameof(ReleaseEvidenceMode));
    public string? ReleaseManifestHash { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeOptionalValue(ReleaseManifestHash);
    public string? ImmutableDeploymentRef { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeOptionalValue(ImmutableDeploymentRef);
    public string SourceAuthority { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(
            SourceAuthority,
            nameof(SourceAuthority));
    public IReadOnlyList<string> PublicEvidenceFiles { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeStringList(PublicEvidenceFiles);
    public IReadOnlyList<string> PublicPrivacyBoundary { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeStringList(PublicPrivacyBoundary);
}

public record ElectionSp10OperationalCustodyEvidenceArtifactRecord(
    string Schema,
    string ElectionId,
    string ProgramVersion,
    string GovernanceMode,
    string CustodyMode,
    string ExecutorKeyLifecycle,
    bool TrusteeThresholdCustodyExpected,
    IReadOnlyList<string> PublicEvidenceFiles,
    IReadOnlyList<string> PublicPrivacyBoundary)
{
    public string Schema { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(Schema, nameof(Schema));
    public string ElectionId { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(ElectionId, nameof(ElectionId));
    public string ProgramVersion { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(
            ProgramVersion,
            nameof(ProgramVersion));
    public string GovernanceMode { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(
            GovernanceMode,
            nameof(GovernanceMode));
    public string CustodyMode { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(CustodyMode, nameof(CustodyMode));
    public string ExecutorKeyLifecycle { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(
            ExecutorKeyLifecycle,
            nameof(ExecutorKeyLifecycle));
    public IReadOnlyList<string> PublicEvidenceFiles { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeStringList(PublicEvidenceFiles);
    public IReadOnlyList<string> PublicPrivacyBoundary { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeStringList(PublicPrivacyBoundary);
}

public record ElectionSp10OperationalVerifierOutputArtifactRecord(
    string ElectionId,
    string VerifierProfileId,
    string Schema,
    DateTime VerifiedAt,
    IReadOnlyList<VerifierCheckResultRecord> Results)
{
    public string ElectionId { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(ElectionId, nameof(ElectionId));
    public string VerifierProfileId { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(
            VerifierProfileId,
            nameof(VerifierProfileId));
    public string Schema { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(Schema, nameof(Schema));
    public IReadOnlyList<VerifierCheckResultRecord> Results { get; init; } =
        Results ?? Array.Empty<VerifierCheckResultRecord>();
}

public record ElectionSp10RestrictedAccessControlSnapshotArtifactRecord(
    string Schema,
    string ElectionId,
    string ProgramVersion,
    string SnapshotId,
    string EvidenceState,
    IReadOnlyList<ElectionSp10RestrictedAccessRoleRecord> Roles,
    IReadOnlyList<string> PublicPrivacyBoundary);

public record ElectionSp10RestrictedAccessRoleRecord(
    string RoleId,
    string Scope,
    int ActorCount,
    string AccessMode,
    string EvidenceHashOrRef);

public record ElectionSp10RestrictedLoggingEvidenceArtifactRecord(
    string Schema,
    string ElectionId,
    string ProgramVersion,
    string LoggingPolicyId,
    IReadOnlyList<string> AllowedEventFamilies,
    IReadOnlyList<string> RestrictedEventFamilies,
    IReadOnlyList<string> ForbiddenEventFamilies,
    string SampleRedactionEvidenceHash,
    IReadOnlyList<string> PublicPrivacyBoundary);

public record ElectionSp10RestrictedBackupRestoreEvidenceArtifactRecord(
    string Schema,
    string ElectionId,
    string ProgramVersion,
    string BackupPolicyId,
    DateTimeOffset LastRestoreTestAt,
    string RestoreTestStatus,
    string EvidenceHashOrRef,
    IReadOnlyList<string> PublicPrivacyBoundary);

public record ElectionSp10RestrictedIncidentEvidenceArtifactRecord(
    string Schema,
    string ElectionId,
    string ProgramVersion,
    string IncidentStatus,
    DateTimeOffset DeclaredAt,
    bool MaterialElectionImpactDeclared,
    string EvidenceHashOrRef,
    IReadOnlyList<string> PublicPrivacyBoundary);

public record ElectionSp10RestrictedAuditorRoomAccessLogArtifactRecord(
    string Schema,
    string ElectionId,
    string ProgramVersion,
    string AccessModel,
    string AccessLogHash,
    int OpenGrantCount,
    IReadOnlyList<string> AuthorizedEvidenceScopes,
    IReadOnlyList<string> PublicPrivacyBoundary);

public record ElectionSp11RegulatoryClaimStateArtifactRecord(
    string Schema,
    string JurisdictionId,
    string ClaimId,
    string TrackerVersion,
    string ClaimState,
    DateTimeOffset SourceCheckedAt,
    DateTimeOffset NextReviewAt,
    string SourceRef,
    string Owner,
    bool IsLegalAdvice,
    bool RequiresAuthorityEvidence,
    string? AuthorityEvidenceRef,
    string? RestrictedWorkpaperRef,
    string AllowedWording,
    IReadOnlyList<string> PublicEvidenceFiles,
    IReadOnlyList<string> RestrictedEvidenceFiles,
    IReadOnlyList<string> PublicPrivacyBoundary)
{
    public string Schema { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(Schema, nameof(Schema));
    public string JurisdictionId { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(
            JurisdictionId,
            nameof(JurisdictionId));
    public string ClaimId { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(ClaimId, nameof(ClaimId));
    public string TrackerVersion { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(
            TrackerVersion,
            nameof(TrackerVersion));
    public string ClaimState { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(ClaimState, nameof(ClaimState));
    public string SourceRef { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(SourceRef, nameof(SourceRef));
    public string Owner { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(Owner, nameof(Owner));
    public string? AuthorityEvidenceRef { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeOptionalValue(AuthorityEvidenceRef);
    public string? RestrictedWorkpaperRef { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeOptionalValue(RestrictedWorkpaperRef);
    public string AllowedWording { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(
            AllowedWording,
            nameof(AllowedWording));
    public IReadOnlyList<string> PublicEvidenceFiles { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeStringList(PublicEvidenceFiles);
    public IReadOnlyList<string> RestrictedEvidenceFiles { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeStringList(RestrictedEvidenceFiles);
    public IReadOnlyList<string> PublicPrivacyBoundary { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeStringList(PublicPrivacyBoundary);
}

public record ElectionSp11RestrictedJurisdictionWorkpaperArtifactRecord(
    string JurisdictionId,
    string ClaimId,
    string TrackerVersion,
    string WorkpaperHash,
    string Owner,
    string ReviewState,
    IReadOnlyList<string> SourceRefs,
    IReadOnlyList<string> PublicPrivacyBoundary)
{
    public string JurisdictionId { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(
            JurisdictionId,
            nameof(JurisdictionId));
    public string ClaimId { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(ClaimId, nameof(ClaimId));
    public string TrackerVersion { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(
            TrackerVersion,
            nameof(TrackerVersion));
    public string WorkpaperHash { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(
            WorkpaperHash,
            nameof(WorkpaperHash));
    public string Owner { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(Owner, nameof(Owner));
    public string ReviewState { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeRequiredValue(
            ReviewState,
            nameof(ReviewState));
    public IReadOnlyList<string> SourceRefs { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeStringList(SourceRefs);
    public IReadOnlyList<string> PublicPrivacyBoundary { get; init; } =
        ElectionSp10OperationalSecurityStatusArtifactRecord.NormalizeStringList(PublicPrivacyBoundary);
}

public static class ElectionSp10OperationalSecurityRules
{
    public static bool IsSupportedEvidenceState(string? evidenceState) =>
        ElectionSp10ProfileIds.OperationalEvidenceStateSet.Contains(Normalize(evidenceState));

    public static bool IsHighAssuranceOperationalClaimAllowed(string? evidenceState) =>
        string.Equals(
            Normalize(evidenceState),
            ElectionSp10ProfileIds.EvidenceStateManagedProfileEvidenceAvailable,
            StringComparison.Ordinal);

    public static bool BlocksHighAssurance(string? evidenceState) =>
        Normalize(evidenceState) switch
        {
            ElectionSp10ProfileIds.EvidenceStateNotAvailable => true,
            ElectionSp10ProfileIds.EvidenceStateDevelopmentPlaceholder => true,
            ElectionSp10ProfileIds.EvidenceStateManagedProfileExceptionDeclared => true,
            ElectionSp10ProfileIds.EvidenceStateBlocked => true,
            _ => false,
        };

    public static string GetPrimaryResultCode(string? evidenceState) =>
        Normalize(evidenceState) switch
        {
            ElectionSp10ProfileIds.EvidenceStateManagedProfileEvidenceAvailable =>
                VerificationResultCodes.OperationalSecurityEvidenceValid,
            ElectionSp10ProfileIds.EvidenceStateManagedProfileDeclared =>
                VerificationResultCodes.OperationalSecurityProfileDeclared,
            ElectionSp10ProfileIds.EvidenceStateDevelopmentPlaceholder =>
                VerificationResultCodes.OperationalSecurityDevelopmentPlaceholder,
            ElectionSp10ProfileIds.EvidenceStateManagedProfileExceptionDeclared =>
                VerificationResultCodes.OperationalSecurityExceptionDeclared,
            ElectionSp10ProfileIds.EvidenceStateBlocked => VerificationResultCodes.OperationalSecurityBlocked,
            _ => VerificationResultCodes.OperationalSecurityEvidenceMissing,
        };

    public static string GetAllowedWordingForEvidenceState(string? evidenceState)
    {
        var normalized = Normalize(evidenceState);
        return ElectionSp10ProfileIds.AllowedWordingByEvidenceState.TryGetValue(normalized, out var wording)
            ? wording
            : ElectionSp10ProfileIds.AllowedWordingByEvidenceState[
                ElectionSp10ProfileIds.EvidenceStateNotAvailable];
    }

    public static bool ContainsForbiddenClaimPhrase(string? text)
    {
        var value = Normalize(text).ToLowerInvariant();
        return value.Length > 0 && ContainsUnsupportedPhrase(value, ElectionSp10ProfileIds.ForbiddenClaimPhrases);
    }

    public static IReadOnlyList<string> Validate(
        ElectionSp10OperationalSecurityStatusArtifactRecord status,
        VerificationPackageView packageView = VerificationPackageView.PublicAnonymous)
    {
        ArgumentNullException.ThrowIfNull(status);

        var errors = new List<string>();
        if (!string.Equals(
                status.Schema,
                ElectionSp10ProfileIds.OperationalSecuritySummarySchema,
                StringComparison.Ordinal))
        {
            errors.Add("schema must be HushVotingOperationalSecuritySummary-v1");
        }

        if (!IsSupportedEvidenceState(status.EvidenceState))
        {
            errors.Add("operational evidence state is unsupported");
        }

        if (status.DoesNotCompleteFeat106Readiness is false)
        {
            errors.Add("operational evidence must explicitly keep FEAT-106 readiness separate");
        }

        if (ContainsForbiddenClaimPhrase(status.Feat106ReadinessCaveat))
        {
            errors.Add("FEAT-106 readiness caveat contains unsupported readiness or certification wording");
        }

        var expectedBlock = BlocksHighAssurance(status.EvidenceState);
        if (status.BlocksHighAssurance != expectedBlock)
        {
            errors.Add($"blocks_high_assurance must be {expectedBlock.ToString().ToLowerInvariant()} for the evidence state");
        }

        if (IsHighAssuranceOperationalClaimAllowed(status.EvidenceState))
        {
            ValidateRequiredOperationalEvidence(status, errors);
        }

        ValidatePublicBoundary(status, packageView, errors);
        return errors.ToArray();
    }

    private static void ValidateRequiredOperationalEvidence(
        ElectionSp10OperationalSecurityStatusArtifactRecord status,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(status.ReleaseManifestHash) ||
            string.IsNullOrWhiteSpace(status.ImmutableDeploymentRef))
        {
            errors.Add("managed operational evidence requires release manifest hash and immutable deployment reference");
        }

        if (!ElectionSp10ProfileIds.CustodyModeSet.Contains(status.CustodyMode ?? string.Empty))
        {
            errors.Add("managed operational evidence requires supported custody mode");
        }

        if (!string.Equals(
                status.ExecutorKeyLifecycle,
                ElectionSp10ProfileIds.ExecutorKeyLifecycleEphemeralMemoryV1,
                StringComparison.Ordinal))
        {
            errors.Add("managed operational evidence requires executor ephemeral memory key lifecycle");
        }

        if (!ElectionSp10ProfileIds.IncidentStatusSet.Contains(status.IncidentStatus ?? string.Empty))
        {
            errors.Add("managed operational evidence requires no-incident or incident-declared status");
        }

        if (string.IsNullOrWhiteSpace(status.AccessSnapshotHashOrRestrictedRef))
        {
            errors.Add("managed operational evidence requires access-control snapshot hash or restricted reference");
        }

        if (string.IsNullOrWhiteSpace(status.BackupRestoreHashOrRestrictedRef))
        {
            errors.Add("managed operational evidence requires backup/restore hash or restricted reference");
        }

        if (string.IsNullOrWhiteSpace(status.AuditorRoomAccessLogHashOrRestrictedRef))
        {
            errors.Add("managed operational evidence requires auditor-room access-log hash or restricted reference");
        }
    }

    private static void ValidatePublicBoundary(
        ElectionSp10OperationalSecurityStatusArtifactRecord status,
        VerificationPackageView packageView,
        List<string> errors)
    {
        var forbiddenFields = VerificationPrivacyBoundary.FindForbiddenPublicFields(status.PublicPrivacyBoundary);
        if (forbiddenFields.Count > 0)
        {
            errors.Add($"public privacy boundary contains forbidden fields: {string.Join(",", forbiddenFields)}");
        }

        if (packageView != VerificationPackageView.PublicAnonymous)
        {
            return;
        }

        var publicRestrictedEntries = status.PublicEvidenceFiles
            .Where(VerificationPrivacyBoundary.IsRestrictedArtifactPath)
            .ToArray();
        if (publicRestrictedEntries.Length > 0)
        {
            errors.Add(
                $"public operational status references restricted evidence: {string.Join(",", publicRestrictedEntries)}");
        }

        if (status.RestrictedEvidenceFiles.Count > 0)
        {
            errors.Add("public operational status must not include restricted evidence files");
        }
    }

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim();

    private static bool ContainsUnsupportedPhrase(string value, IEnumerable<string> phrases) =>
        phrases.Any(phrase =>
            value.Contains(phrase, StringComparison.Ordinal) &&
            !value.Contains($"not {phrase}", StringComparison.Ordinal) &&
            !value.Contains($"no {phrase}", StringComparison.Ordinal) &&
            !value.Contains($"without {phrase}", StringComparison.Ordinal));
}

public static class ElectionSp11RegulatoryRules
{
    public static bool IsSupportedClaimState(string? claimState) =>
        ElectionSp11ProfileIds.ClaimStateSet.Contains(Normalize(claimState));

    public static bool IsTrackerStale(
        ElectionSp11RegulatoryClaimStateArtifactRecord claim,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(claim);

        return claim.NextReviewAt < observedAt;
    }

    public static string GetAllowedWordingForClaimState(string? claimState)
    {
        var normalized = Normalize(claimState);
        return ElectionSp11ProfileIds.AllowedWordingByClaimState.TryGetValue(normalized, out var wording)
            ? wording
            : ElectionSp11ProfileIds.AllowedWordingByClaimState[ElectionSp11ProfileIds.ClaimStateForbidden];
    }

    public static bool ContainsForbiddenClaimPhrase(string? text)
    {
        var value = Normalize(text).ToLowerInvariant();
        return value.Length > 0 && ContainsUnsupportedPhrase(value, ElectionSp11ProfileIds.ForbiddenClaimPhrases);
    }

    public static IReadOnlyList<string> Validate(
        ElectionSp11RegulatoryClaimStateArtifactRecord claim,
        VerificationPackageView packageView = VerificationPackageView.PublicAnonymous,
        DateTimeOffset? observedAt = null)
    {
        ArgumentNullException.ThrowIfNull(claim);

        var errors = new List<string>();
        if (!string.Equals(claim.Schema, ElectionSp11ProfileIds.RegulatoryClaimStateSchema, StringComparison.Ordinal))
        {
            errors.Add("schema must be HushVotingRegulatoryClaimState-v1");
        }

        if (!IsSupportedClaimState(claim.ClaimState))
        {
            errors.Add("regulatory claim state is unsupported");
        }

        if (claim.IsLegalAdvice)
        {
            errors.Add("regulatory tracker must be market intelligence, not legal advice");
        }

        if (ContainsForbiddenClaimPhrase(claim.AllowedWording))
        {
            errors.Add("regulatory wording contains unsupported legal, certification, or authority-approval language");
        }

        if (claim.RequiresAuthorityEvidence &&
            (string.Equals(
                    claim.ClaimState,
                    ElectionSp11ProfileIds.ClaimStateAllowedNow,
                    StringComparison.Ordinal) ||
                string.Equals(
                    claim.ClaimState,
                    ElectionSp11ProfileIds.ClaimStateAllowedWithLimitation,
                    StringComparison.Ordinal)) &&
            string.IsNullOrWhiteSpace(claim.AuthorityEvidenceRef))
        {
            errors.Add("authority-gated regulatory claim cannot be allowed without authority evidence");
        }

        if (IsTrackerStale(claim, observedAt ?? DateTimeOffset.UtcNow))
        {
            errors.Add("regulatory tracker is stale and cannot support new claims without review");
        }

        ValidatePublicBoundary(claim, packageView, errors);
        return errors.ToArray();
    }

    private static void ValidatePublicBoundary(
        ElectionSp11RegulatoryClaimStateArtifactRecord claim,
        VerificationPackageView packageView,
        List<string> errors)
    {
        var forbiddenFields = VerificationPrivacyBoundary.FindForbiddenPublicFields(claim.PublicPrivacyBoundary);
        if (forbiddenFields.Count > 0)
        {
            errors.Add($"public privacy boundary contains forbidden fields: {string.Join(",", forbiddenFields)}");
        }

        if (packageView != VerificationPackageView.PublicAnonymous)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(claim.RestrictedWorkpaperRef))
        {
            errors.Add("public regulatory claim state must not include restricted workpaper references");
        }

        var publicRestrictedEntries = claim.PublicEvidenceFiles
            .Where(VerificationPrivacyBoundary.IsRestrictedArtifactPath)
            .ToArray();
        if (publicRestrictedEntries.Length > 0)
        {
            errors.Add(
                $"public regulatory claim references restricted evidence: {string.Join(",", publicRestrictedEntries)}");
        }

        if (claim.RestrictedEvidenceFiles.Count > 0)
        {
            errors.Add("public regulatory claim state must not include restricted evidence files");
        }
    }

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim();

    private static bool ContainsUnsupportedPhrase(string value, IEnumerable<string> phrases) =>
        phrases.Any(phrase =>
            value.Contains(phrase, StringComparison.Ordinal) &&
            !value.Contains($"not {phrase}", StringComparison.Ordinal) &&
            !value.Contains($"no {phrase}", StringComparison.Ordinal) &&
            !value.Contains($"without {phrase}", StringComparison.Ordinal));
}
