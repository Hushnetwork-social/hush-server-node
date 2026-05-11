using System.Text.Json;

namespace HushShared.Elections.Verification.Model;

public static class ElectionSp08ProfileIds
{
    public const string ReleaseManifestSchema = "HushVotingReleaseManifest-v1";
    public const string ReleaseManifestFileName = "HushVotingReleaseManifest-v1.json";
    public const string ReleaseIntegritySchema = "HushVotingReleaseIntegrity-v1";
    public const string ReleaseVerifierOutputSchema = "HushVotingReleaseIntegrityVerifierOutput-v1";

    public const string EvidenceModeDevelopmentPlaceholder = "development_placeholder";
    public const string EvidenceModeOfficial = "official_sp08";

    public const string ServerComponent = "server";
    public const string WebClientComponent = "web_client";
    public const string StandaloneVerifierComponent = "standalone_verifier";
    public const string Sp07ProofWorkerComponent = "sp07_proof_worker";
    public const string ProtocolPackageComponent = "protocol_package";
    public const string AuditPackageExporterComponent = "audit_package_exporter";
    public const string MobileAppComponent = "mobile_app";

    public const string OpenLifecycleStage = "open_policy";
    public const string CloseLifecycleStage = "close_observed";
    public const string ProofWorkerLifecycleStage = "proof_worker_observed";
    public const string ExporterLifecycleStage = "exporter_observed";
    public const string ClientReleaseSetLifecycleStage = "client_release_set";

    public const string ReleaseIntegrityAcceptedCheckCode = "REL-000";
    public const string ManifestRequiredCheckCode = "REL-001";
    public const string EvidenceModeAllowedCheckCode = "REL-002";
    public const string ImmutableArtifactsCheckCode = "REL-003";
    public const string ComponentHashesCheckCode = "REL-004";
    public const string CircuitAndPackageHashesCheckCode = "REL-005";
    public const string LifecycleBindingCheckCode = "REL-006";
    public const string MobileEvidenceCheckCode = "REL-007";
    public const string PackageMembershipCheckCode = "REL-008";

    public static IReadOnlySet<string> EvidenceModes { get; } = new HashSet<string>(
        [
            EvidenceModeDevelopmentPlaceholder,
            EvidenceModeOfficial,
        ],
        StringComparer.Ordinal);

    public static IReadOnlyList<string> RequiredHighAssuranceComponentIds { get; } =
    [
        ServerComponent,
        WebClientComponent,
        StandaloneVerifierComponent,
        Sp07ProofWorkerComponent,
        ProtocolPackageComponent,
        AuditPackageExporterComponent,
    ];

    public static IReadOnlyList<string> ReleaseIntegrityCheckCodes { get; } =
    [
        ReleaseIntegrityAcceptedCheckCode,
        ManifestRequiredCheckCode,
        EvidenceModeAllowedCheckCode,
        ImmutableArtifactsCheckCode,
        ComponentHashesCheckCode,
        CircuitAndPackageHashesCheckCode,
        LifecycleBindingCheckCode,
        MobileEvidenceCheckCode,
        PackageMembershipCheckCode,
    ];
}

public record ElectionSp08ReleaseManifestArtifactRecord(
    string Schema,
    string ManifestId,
    string ReleaseId,
    string EvidenceMode,
    bool NotForReleaseIntegrityClaims,
    DateTime GeneratedAt,
    string SourceAuthority,
    string SourceCommit,
    string SourceTag,
    IReadOnlyList<ElectionSp08ReleaseComponentArtifactRecord> Components,
    IReadOnlyList<ElectionSp08CircuitKeyArtifactRecord> CircuitAndKeys,
    IReadOnlyList<ElectionSp08LifecycleReleaseBindingRecord> LifecycleBindings,
    IReadOnlyList<string> PublicPrivacyBoundary)
{
    public string Schema { get; init; } = NormalizeRequiredValue(Schema, nameof(Schema));
    public string ManifestId { get; init; } = NormalizeRequiredValue(ManifestId, nameof(ManifestId));
    public string ReleaseId { get; init; } = NormalizeRequiredValue(ReleaseId, nameof(ReleaseId));
    public string EvidenceMode { get; init; } = NormalizeRequiredValue(EvidenceMode, nameof(EvidenceMode));
    public string SourceAuthority { get; init; } = NormalizeRequiredValue(SourceAuthority, nameof(SourceAuthority));
    public string SourceCommit { get; init; } = NormalizeRequiredValue(SourceCommit, nameof(SourceCommit));
    public string SourceTag { get; init; } = NormalizeRequiredValue(SourceTag, nameof(SourceTag));

    public IReadOnlyList<ElectionSp08ReleaseComponentArtifactRecord> Components { get; init; } =
        Components?.ToArray() ?? [];

    public IReadOnlyList<ElectionSp08CircuitKeyArtifactRecord> CircuitAndKeys { get; init; } =
        CircuitAndKeys?.ToArray() ?? [];

    public IReadOnlyList<ElectionSp08LifecycleReleaseBindingRecord> LifecycleBindings { get; init; } =
        LifecycleBindings?.ToArray() ?? [];

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

public record ElectionSp08ReleaseComponentArtifactRecord(
    string ComponentId,
    string ComponentType,
    string EvidenceMode,
    string ArtifactName,
    string ArtifactDigest,
    string SourceCommit,
    string SourceTag,
    string ImmutableReference,
    string? BuildWorkflowRunId,
    string? DistributionReference,
    string? SigningFingerprint,
    bool IsPlaceholder)
{
    public string ComponentId { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(ComponentId, nameof(ComponentId));

    public string ComponentType { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(ComponentType, nameof(ComponentType));

    public string EvidenceMode { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(EvidenceMode, nameof(EvidenceMode));

    public string ArtifactName { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(ArtifactName, nameof(ArtifactName));

    public string ArtifactDigest { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(ArtifactDigest, nameof(ArtifactDigest));

    public string SourceCommit { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(SourceCommit, nameof(SourceCommit));

    public string SourceTag { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(SourceTag, nameof(SourceTag));

    public string ImmutableReference { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(ImmutableReference, nameof(ImmutableReference));

    public string? BuildWorkflowRunId { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeOptionalValue(BuildWorkflowRunId);

    public string? DistributionReference { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeOptionalValue(DistributionReference);

    public string? SigningFingerprint { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeOptionalValue(SigningFingerprint);
}

public record ElectionSp08CircuitKeyArtifactRecord(
    string CircuitId,
    string CircuitHash,
    string ProvingKeyHash,
    string VerifyingKeyHash,
    string ProtocolPackageManifestHash)
{
    public string CircuitId { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(CircuitId, nameof(CircuitId));

    public string CircuitHash { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(CircuitHash, nameof(CircuitHash));

    public string ProvingKeyHash { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(ProvingKeyHash, nameof(ProvingKeyHash));

    public string VerifyingKeyHash { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(VerifyingKeyHash, nameof(VerifyingKeyHash));

    public string ProtocolPackageManifestHash { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(
            ProtocolPackageManifestHash,
            nameof(ProtocolPackageManifestHash));
}

public record ElectionSp08LifecycleReleaseBindingRecord(
    string LifecycleStage,
    string ExpectedReleaseId,
    string ObservedReleaseId,
    string ExpectedArtifactDigest,
    string ObservedArtifactDigest,
    bool MatchesSealedPolicy)
{
    public string LifecycleStage { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(LifecycleStage, nameof(LifecycleStage));

    public string ExpectedReleaseId { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(ExpectedReleaseId, nameof(ExpectedReleaseId));

    public string ObservedReleaseId { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(ObservedReleaseId, nameof(ObservedReleaseId));

    public string ExpectedArtifactDigest { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(
            ExpectedArtifactDigest,
            nameof(ExpectedArtifactDigest));

    public string ObservedArtifactDigest { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(
            ObservedArtifactDigest,
            nameof(ObservedArtifactDigest));
}

public record ElectionSp08ReleaseIntegrityArtifactRecord(
    string ElectionId,
    string ProfileId,
    string EvidenceMode,
    bool NotForReleaseIntegrityClaims,
    bool BlocksHighAssurance,
    string ReleaseManifestName,
    string ReleaseManifestHash,
    string ProtocolPackageManifestName,
    string ProtocolPackageManifestHash,
    string PrimaryResultCode,
    IReadOnlyList<ElectionSp08ReleaseComponentArtifactRecord> Components,
    IReadOnlyList<ElectionSp08LifecycleReleaseBindingRecord> LifecycleBindings,
    IReadOnlyList<string> PublicPrivacyBoundary)
{
    public string ElectionId { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(ElectionId, nameof(ElectionId));

    public string ProfileId { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(ProfileId, nameof(ProfileId));

    public string EvidenceMode { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(EvidenceMode, nameof(EvidenceMode));

    public string ReleaseManifestName { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(
            ReleaseManifestName,
            nameof(ReleaseManifestName));

    public string ReleaseManifestHash { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(
            ReleaseManifestHash,
            nameof(ReleaseManifestHash));

    public string ProtocolPackageManifestName { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(
            ProtocolPackageManifestName,
            nameof(ProtocolPackageManifestName));

    public string ProtocolPackageManifestHash { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(
            ProtocolPackageManifestHash,
            nameof(ProtocolPackageManifestHash));

    public string PrimaryResultCode { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(PrimaryResultCode, nameof(PrimaryResultCode));

    public IReadOnlyList<ElectionSp08ReleaseComponentArtifactRecord> Components { get; init; } =
        Components?.ToArray() ?? [];

    public IReadOnlyList<ElectionSp08LifecycleReleaseBindingRecord> LifecycleBindings { get; init; } =
        LifecycleBindings?.ToArray() ?? [];

    public IReadOnlyList<string> PublicPrivacyBoundary { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeStringList(PublicPrivacyBoundary);
}

public record ElectionSp08VerifierOutputArtifactRecord(
    string ElectionId,
    string VerifierProfileId,
    string Schema,
    DateTime VerifiedAt,
    IReadOnlyList<VerifierCheckResultRecord> Results)
{
    public string ElectionId { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(ElectionId, nameof(ElectionId));

    public string VerifierProfileId { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(VerifierProfileId, nameof(VerifierProfileId));

    public string Schema { get; init; } =
        ElectionSp08ReleaseManifestArtifactRecord.NormalizeRequiredValue(Schema, nameof(Schema));

    public IReadOnlyList<VerifierCheckResultRecord> Results { get; init; } =
        Results?.ToArray() ?? [];
}

public static class ElectionSp08ReleaseIntegrityRules
{
    public static bool IsOfficialEvidenceMode(string? evidenceMode) =>
        string.Equals(evidenceMode, ElectionSp08ProfileIds.EvidenceModeOfficial, StringComparison.Ordinal);

    public static bool IsDevelopmentPlaceholder(string? evidenceMode) =>
        string.Equals(evidenceMode, ElectionSp08ProfileIds.EvidenceModeDevelopmentPlaceholder, StringComparison.Ordinal);

    public static bool IsEvidenceModeAllowedForProfile(
        string profileId,
        string evidenceMode,
        bool notForReleaseIntegrityClaims)
    {
        if (!string.Equals(profileId, VerificationProfileIds.HighAssuranceV1, StringComparison.Ordinal))
        {
            return true;
        }

        return IsOfficialEvidenceMode(evidenceMode) && !notForReleaseIntegrityClaims;
    }

    public static bool IsMutableOrLocalReference(string? artifactReference)
    {
        var value = (artifactReference ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return true;
        }

        var lower = value.ToLowerInvariant();
        return lower == "latest" ||
            lower.EndsWith(":latest", StringComparison.Ordinal) ||
            lower.Contains("/latest", StringComparison.Ordinal) ||
            lower.Contains("refs/heads/", StringComparison.Ordinal) ||
            lower is "main" or "master" or "develop" or "dev" ||
            lower.StartsWith("file:", StringComparison.Ordinal) ||
            lower.StartsWith("localhost", StringComparison.Ordinal) ||
            lower.Contains("download", StringComparison.Ordinal);
    }

    public static bool IsImmutableReference(string? artifactReference) =>
        !IsMutableOrLocalReference(artifactReference);
}

public static class ElectionSp08ReleaseManifestHasher
{
    public static string ComputeReleaseManifestHash(ElectionSp08ReleaseManifestArtifactRecord manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var canonical = Canonicalize(manifest);

        return VerificationCanonicalHash.ComputeSha256LowerHex(
            JsonSerializer.Serialize(canonical, VerificationJson.Options));
    }

    public static ElectionSp08ReleaseManifestArtifactRecord Canonicalize(
        ElectionSp08ReleaseManifestArtifactRecord manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return manifest with
        {
            Components = manifest.Components
                .OrderBy(x => x.ComponentId, StringComparer.Ordinal)
                .ThenBy(x => x.ArtifactDigest, StringComparer.Ordinal)
                .ToArray(),
            CircuitAndKeys = manifest.CircuitAndKeys
                .OrderBy(x => x.CircuitId, StringComparer.Ordinal)
                .ToArray(),
            LifecycleBindings = manifest.LifecycleBindings
                .OrderBy(x => x.LifecycleStage, StringComparer.Ordinal)
                .ToArray(),
            PublicPrivacyBoundary = manifest.PublicPrivacyBoundary
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray(),
        };
    }
}

public static class ElectionSp08ReleaseManifestGenerator
{
    public static ElectionSp08ReleaseManifestArtifactRecord Generate(
        ElectionSp08ReleaseManifestArtifactRecord manifest)
    {
        var errors = Validate(manifest);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Release manifest input is invalid: {string.Join("; ", errors)}");
        }

        return ElectionSp08ReleaseManifestHasher.Canonicalize(manifest);
    }

    public static string SerializeCanonical(ElectionSp08ReleaseManifestArtifactRecord manifest) =>
        JsonSerializer.Serialize(Generate(manifest), VerificationJson.Options);

    public static IReadOnlyList<string> Validate(ElectionSp08ReleaseManifestArtifactRecord manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var errors = new List<string>();
        if (!string.Equals(manifest.Schema, ElectionSp08ProfileIds.ReleaseManifestSchema, StringComparison.Ordinal))
        {
            errors.Add("schema must be HushVotingReleaseManifest-v1");
        }

        if (!ElectionSp08ProfileIds.EvidenceModes.Contains(manifest.EvidenceMode))
        {
            errors.Add("evidence mode is unsupported");
        }

        if (ElectionSp08ReleaseIntegrityRules.IsDevelopmentPlaceholder(manifest.EvidenceMode) &&
            !manifest.NotForReleaseIntegrityClaims)
        {
            errors.Add("development_placeholder must set not_for_release_integrity_claims");
        }

        if (ElectionSp08ReleaseIntegrityRules.IsOfficialEvidenceMode(manifest.EvidenceMode))
        {
            ValidateOfficialManifest(manifest, errors);
        }

        var forbiddenPublicFields = VerificationPrivacyBoundary.FindForbiddenPublicFields(
            manifest.PublicPrivacyBoundary);
        if (forbiddenPublicFields.Count > 0)
        {
            errors.Add($"public privacy boundary contains forbidden fields: {string.Join(",", forbiddenPublicFields)}");
        }

        return errors.ToArray();
    }

    private static void ValidateOfficialManifest(
        ElectionSp08ReleaseManifestArtifactRecord manifest,
        List<string> errors)
    {
        if (manifest.NotForReleaseIntegrityClaims)
        {
            errors.Add("official_sp08 must not set not_for_release_integrity_claims");
        }

        var componentIds = manifest.Components
            .Select(x => x.ComponentId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var requiredComponent in ElectionSp08ProfileIds.RequiredHighAssuranceComponentIds)
        {
            if (!componentIds.Contains(requiredComponent))
            {
                errors.Add($"official_sp08 missing required component {requiredComponent}");
            }
        }

        foreach (var component in manifest.Components)
        {
            if (component.IsPlaceholder)
            {
                errors.Add($"official_sp08 component {component.ComponentId} is marked placeholder");
            }

            if (!ElectionSp08ReleaseIntegrityRules.IsOfficialEvidenceMode(component.EvidenceMode))
            {
                errors.Add($"official_sp08 component {component.ComponentId} is not official evidence");
            }

            if (ElectionSp08ReleaseIntegrityRules.IsMutableOrLocalReference(component.ImmutableReference))
            {
                errors.Add($"official_sp08 component {component.ComponentId} uses a mutable or local reference");
            }

            if (!IsSha256Prefixed(component.ArtifactDigest))
            {
                errors.Add($"official_sp08 component {component.ComponentId} artifact digest must be sha256-prefixed");
            }

            if (string.IsNullOrWhiteSpace(component.BuildWorkflowRunId))
            {
                errors.Add($"official_sp08 component {component.ComponentId} must include build workflow run id");
            }
        }

        if (manifest.CircuitAndKeys.Count == 0)
        {
            errors.Add("official_sp08 requires circuit and key evidence");
        }

        foreach (var circuitAndKey in manifest.CircuitAndKeys)
        {
            if (!IsSha256Prefixed(circuitAndKey.CircuitHash))
            {
                errors.Add($"official_sp08 circuit {circuitAndKey.CircuitId} circuit hash must be sha256-prefixed");
            }

            if (!IsSha256Prefixed(circuitAndKey.ProvingKeyHash))
            {
                errors.Add($"official_sp08 circuit {circuitAndKey.CircuitId} proving-key hash must be sha256-prefixed");
            }

            if (!IsSha256Prefixed(circuitAndKey.VerifyingKeyHash))
            {
                errors.Add($"official_sp08 circuit {circuitAndKey.CircuitId} verifying-key hash must be sha256-prefixed");
            }
        }

        var lifecycleStages = manifest.LifecycleBindings
            .Select(x => x.LifecycleStage)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var requiredStage in new[]
                 {
                     ElectionSp08ProfileIds.OpenLifecycleStage,
                     ElectionSp08ProfileIds.CloseLifecycleStage,
                     ElectionSp08ProfileIds.ProofWorkerLifecycleStage,
                     ElectionSp08ProfileIds.ExporterLifecycleStage,
                     ElectionSp08ProfileIds.ClientReleaseSetLifecycleStage,
                 })
        {
            if (!lifecycleStages.Contains(requiredStage))
            {
                errors.Add($"official_sp08 missing lifecycle binding {requiredStage}");
            }
        }

        foreach (var lifecycleBinding in manifest.LifecycleBindings)
        {
            if (!lifecycleBinding.MatchesSealedPolicy ||
                !string.Equals(lifecycleBinding.ExpectedReleaseId, lifecycleBinding.ObservedReleaseId, StringComparison.Ordinal) ||
                !string.Equals(lifecycleBinding.ExpectedArtifactDigest, lifecycleBinding.ObservedArtifactDigest, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"official_sp08 lifecycle binding {lifecycleBinding.LifecycleStage} does not match sealed policy");
            }
        }
    }

    private static bool IsSha256Prefixed(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase);
}
