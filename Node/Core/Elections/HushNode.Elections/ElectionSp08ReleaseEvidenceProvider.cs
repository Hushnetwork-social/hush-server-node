using System.Text.Json;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;

namespace HushNode.Elections;

public interface IElectionSp08ReleaseEvidenceProvider
{
    Task<ElectionSp08ReleaseEvidenceSnapshot> GetOpenReadinessEvidenceAsync(
        ElectionRecord election,
        ProtocolPackageBindingOpenValidation protocolPackageValidation,
        CancellationToken cancellationToken = default);
}

public sealed record ElectionSp08ReleaseEvidenceOptions(
    string? OpenReadinessReleaseManifestPath = null);

public sealed record ElectionSp08ReleaseEvidenceSnapshot(
    bool PublicEvidenceAvailable,
    bool RestrictedEvidenceAvailable,
    string EvidenceMode,
    bool NotForReleaseIntegrityClaims,
    string ReleaseManifestName,
    string ReleaseManifestHash,
    string ProtocolPackageManifestName,
    string ProtocolPackageManifestHash,
    string PrimaryResultCode,
    string PrimaryIssue,
    IReadOnlyList<ElectionSp08ReleaseComponentArtifactRecord> Components,
    IReadOnlyList<ElectionSp08LifecycleReleaseBindingRecord> LifecycleBindings,
    int EvidenceFileCount)
{
    public bool MobileEvidenceIncluded =>
        Components.Any(x => string.Equals(x.ComponentId, ElectionSp08ProfileIds.MobileAppComponent, StringComparison.Ordinal));

    public bool SatisfiesOfficialReleaseIntegrity =>
        PublicEvidenceAvailable &&
        ElectionSp08ReleaseIntegrityRules.IsOfficialEvidenceMode(EvidenceMode) &&
        !NotForReleaseIntegrityClaims &&
        string.Equals(PrimaryResultCode, VerificationResultCodes.ReleaseIntegrityEvidenceValid, StringComparison.Ordinal) &&
        LifecycleBindings.All(x => x.MatchesSealedPolicy);
}

public sealed class ElectionSp08ReleaseEvidenceProvider : IElectionSp08ReleaseEvidenceProvider
{
    private readonly ElectionSp08ReleaseEvidenceOptions _options;

    public ElectionSp08ReleaseEvidenceProvider()
        : this(new ElectionSp08ReleaseEvidenceOptions())
    {
    }

    public ElectionSp08ReleaseEvidenceProvider(ElectionSp08ReleaseEvidenceOptions options)
    {
        _options = options;
    }

    public async Task<ElectionSp08ReleaseEvidenceSnapshot> GetOpenReadinessEvidenceAsync(
        ElectionRecord election,
        ProtocolPackageBindingOpenValidation protocolPackageValidation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(election);
        ArgumentNullException.ThrowIfNull(protocolPackageValidation);

        if (string.IsNullOrWhiteSpace(_options.OpenReadinessReleaseManifestPath))
        {
            return CreateDevelopmentPlaceholder(protocolPackageValidation);
        }

        var manifestPath = _options.OpenReadinessReleaseManifestPath.Trim();
        if (!File.Exists(manifestPath))
        {
            return CreateMissing(
                protocolPackageValidation,
                $"Configured SP-08 release manifest was not found at '{manifestPath}'.");
        }

        try
        {
            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<ElectionSp08ReleaseManifestArtifactRecord>(
                manifestJson,
                VerificationJson.Options);

            if (manifest is null)
            {
                return CreateMissing(
                    protocolPackageValidation,
                    "Configured SP-08 release manifest is empty or malformed.");
            }

            return CreateFromManifest(protocolPackageValidation, manifest);
        }
        catch (JsonException ex)
        {
            return CreateMissing(
                protocolPackageValidation,
                $"Configured SP-08 release manifest could not be parsed: {ex.Message}");
        }
        catch (IOException ex)
        {
            return CreateMissing(
                protocolPackageValidation,
                $"Configured SP-08 release manifest could not be read: {ex.Message}");
        }
    }

    public static ElectionSp08ReleaseEvidenceSnapshot CreateDevelopmentPlaceholder(
        ProtocolPackageBindingOpenValidation protocolPackageValidation) =>
        new(
            PublicEvidenceAvailable: false,
            RestrictedEvidenceAvailable: false,
            EvidenceMode: ElectionSp08ProfileIds.EvidenceModeDevelopmentPlaceholder,
            NotForReleaseIntegrityClaims: true,
            ReleaseManifestName: ElectionSp08ProfileIds.ReleaseManifestFileName,
            ReleaseManifestHash: string.Empty,
            ProtocolPackageManifestName: ProtocolPackagePromotionService.ReleaseManifestFileName,
            ProtocolPackageManifestHash: protocolPackageValidation.Binding?.ReleaseManifestHash ?? string.Empty,
            PrimaryResultCode: VerificationResultCodes.ReleaseIntegrityEvidencePending,
            PrimaryIssue:
                "Development placeholder SP-08 release evidence is present for this development profile. It is not official release evidence and must not support release-integrity claims.",
            Components: [],
            LifecycleBindings: [],
            EvidenceFileCount: 0);

    public static ElectionSp08ReleaseEvidenceSnapshot CreateMissing(
        ProtocolPackageBindingOpenValidation protocolPackageValidation,
        string primaryIssue) =>
        new(
            PublicEvidenceAvailable: false,
            RestrictedEvidenceAvailable: false,
            EvidenceMode: string.Empty,
            NotForReleaseIntegrityClaims: false,
            ReleaseManifestName: ElectionSp08ProfileIds.ReleaseManifestFileName,
            ReleaseManifestHash: string.Empty,
            ProtocolPackageManifestName: ProtocolPackagePromotionService.ReleaseManifestFileName,
            ProtocolPackageManifestHash: protocolPackageValidation.Binding?.ReleaseManifestHash ?? string.Empty,
            PrimaryResultCode: VerificationResultCodes.ReleaseIntegrityManifestMissing,
            PrimaryIssue: primaryIssue,
            Components: [],
            LifecycleBindings: [],
            EvidenceFileCount: 0);

    public static ElectionSp08ReleaseEvidenceSnapshot CreateFromManifest(
        ProtocolPackageBindingOpenValidation protocolPackageValidation,
        ElectionSp08ReleaseManifestArtifactRecord manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var canonicalManifest = ElectionSp08ReleaseManifestHasher.Canonicalize(manifest);
        var manifestHash = ElectionSp08ReleaseManifestHasher.ComputeReleaseManifestHash(canonicalManifest);
        var protocolPackageManifestHash = protocolPackageValidation.Binding?.ReleaseManifestHash ?? string.Empty;
        var evaluation = EvaluateManifest(canonicalManifest, protocolPackageManifestHash);

        return new ElectionSp08ReleaseEvidenceSnapshot(
            PublicEvidenceAvailable: true,
            RestrictedEvidenceAvailable: false,
            EvidenceMode: canonicalManifest.EvidenceMode,
            NotForReleaseIntegrityClaims: canonicalManifest.NotForReleaseIntegrityClaims,
            ReleaseManifestName: ElectionSp08ProfileIds.ReleaseManifestFileName,
            ReleaseManifestHash: manifestHash,
            ProtocolPackageManifestName: ProtocolPackagePromotionService.ReleaseManifestFileName,
            ProtocolPackageManifestHash: protocolPackageManifestHash,
            PrimaryResultCode: evaluation.PrimaryResultCode,
            PrimaryIssue: evaluation.PrimaryIssue,
            Components: canonicalManifest.Components,
            LifecycleBindings: canonicalManifest.LifecycleBindings,
            EvidenceFileCount: 1);
    }

    private static Sp08ManifestEvaluation EvaluateManifest(
        ElectionSp08ReleaseManifestArtifactRecord manifest,
        string protocolPackageManifestHash)
    {
        if (ElectionSp08ReleaseIntegrityRules.IsDevelopmentPlaceholder(manifest.EvidenceMode) ||
            manifest.NotForReleaseIntegrityClaims)
        {
            return new(
                VerificationResultCodes.ReleaseIntegrityEvidencePending,
                "Development placeholder SP-08 release evidence is present. It is not official release evidence and must not support release-integrity claims.");
        }

        if (!ElectionSp08ReleaseIntegrityRules.IsOfficialEvidenceMode(manifest.EvidenceMode))
        {
            return new(
                VerificationResultCodes.ReleaseIntegrityEvidenceModeNotAllowed,
                "SP-08 release evidence mode is not allowed for official release-integrity claims.");
        }

        var validationError = ElectionSp08ReleaseManifestGenerator.Validate(manifest).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return new(
                MapValidationErrorToResultCode(validationError),
                $"SP-08 release manifest is invalid: {validationError}");
        }

        var duplicateComponent = manifest.Components
            .GroupBy(x => x.ComponentId, StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicateComponent is not null)
        {
            return new(
                VerificationResultCodes.ReleaseIntegrityComponentHashMismatch,
                $"SP-08 release manifest has duplicate component id '{duplicateComponent.Key}'.");
        }

        var componentIds = manifest.Components
            .Select(x => x.ComponentId)
            .ToHashSet(StringComparer.Ordinal);
        var missingComponent = ElectionSp08ProfileIds.RequiredHighAssuranceComponentIds
            .FirstOrDefault(componentId => !componentIds.Contains(componentId));
        if (!string.IsNullOrWhiteSpace(missingComponent))
        {
            return new(
                VerificationResultCodes.ReleaseIntegrityComponentHashMismatch,
                $"SP-08 release manifest is missing required component '{missingComponent}'.");
        }

        var malformedComponent = manifest.Components.FirstOrDefault(component =>
            component.IsPlaceholder ||
            !ElectionSp08ReleaseIntegrityRules.IsOfficialEvidenceMode(component.EvidenceMode) ||
            string.IsNullOrWhiteSpace(component.ArtifactDigest) ||
            !component.ArtifactDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase));
        if (malformedComponent is not null)
        {
            return new(
                VerificationResultCodes.ReleaseIntegrityComponentHashMismatch,
                $"SP-08 component '{malformedComponent.ComponentId}' does not bind official source and artifact hashes.");
        }

        var mutableComponent = manifest.Components.FirstOrDefault(component =>
            ElectionSp08ReleaseIntegrityRules.IsMutableOrLocalReference(component.ImmutableReference));
        if (mutableComponent is not null)
        {
            return new(
                VerificationResultCodes.ReleaseIntegrityMutableArtifactReference,
                $"SP-08 component '{mutableComponent.ComponentId}' uses a mutable or local artifact reference.");
        }

        var protocolBindingMatches =
            !string.IsNullOrWhiteSpace(protocolPackageManifestHash) &&
            manifest.CircuitAndKeys.Any(x =>
                string.Equals(x.ProtocolPackageManifestHash, protocolPackageManifestHash, StringComparison.OrdinalIgnoreCase));
        var malformedCircuitEvidence = manifest.CircuitAndKeys.FirstOrDefault(x =>
            string.IsNullOrWhiteSpace(x.CircuitHash) ||
            string.IsNullOrWhiteSpace(x.ProvingKeyHash) ||
            string.IsNullOrWhiteSpace(x.VerifyingKeyHash) ||
            !x.CircuitHash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
            !x.ProvingKeyHash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
            !x.VerifyingKeyHash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase));
        if (!protocolBindingMatches || malformedCircuitEvidence is not null)
        {
            return new(
                VerificationResultCodes.ReleaseIntegrityCircuitOrPackageHashMismatch,
                "SP-08 protocol package or circuit/key evidence does not bind the selected Protocol Omega package.");
        }

        var lifecycleStages = manifest.LifecycleBindings
            .Select(x => x.LifecycleStage)
            .ToHashSet(StringComparer.Ordinal);
        var missingLifecycleStage = new[]
            {
                ElectionSp08ProfileIds.OpenLifecycleStage,
                ElectionSp08ProfileIds.CloseLifecycleStage,
                ElectionSp08ProfileIds.ProofWorkerLifecycleStage,
                ElectionSp08ProfileIds.ExporterLifecycleStage,
                ElectionSp08ProfileIds.ClientReleaseSetLifecycleStage,
            }
            .FirstOrDefault(stage => !lifecycleStages.Contains(stage));
        if (!string.IsNullOrWhiteSpace(missingLifecycleStage))
        {
            return new(
                VerificationResultCodes.ReleaseIntegrityLifecycleMismatch,
                $"SP-08 release manifest is missing lifecycle binding '{missingLifecycleStage}'.");
        }

        var lifecycleMismatch = manifest.LifecycleBindings.FirstOrDefault(x =>
            !x.MatchesSealedPolicy ||
            !string.Equals(x.ExpectedReleaseId, x.ObservedReleaseId, StringComparison.Ordinal) ||
            !string.Equals(x.ExpectedArtifactDigest, x.ObservedArtifactDigest, StringComparison.OrdinalIgnoreCase));
        if (lifecycleMismatch is not null)
        {
            return new(
                VerificationResultCodes.ReleaseIntegrityLifecycleMismatch,
                $"SP-08 lifecycle release binding '{lifecycleMismatch.LifecycleStage}' does not match the sealed release policy.");
        }

        var mobileEvidenceIncomplete = manifest.Components.Any(component =>
            string.Equals(component.ComponentId, ElectionSp08ProfileIds.MobileAppComponent, StringComparison.Ordinal) &&
            (string.IsNullOrWhiteSpace(component.DistributionReference) ||
             string.IsNullOrWhiteSpace(component.SigningFingerprint)));
        if (mobileEvidenceIncomplete)
        {
            return new(
                VerificationResultCodes.ReleaseIntegrityMobileEvidenceIncomplete,
                "SP-08 mobile release evidence is missing distribution or signing evidence.");
        }

        return new(
            VerificationResultCodes.ReleaseIntegrityEvidenceValid,
            "Official SP-08 release-integrity evidence is ready for election open.");
    }

    private static string MapValidationErrorToResultCode(string validationError)
    {
        if (validationError.Contains("mutable or local reference", StringComparison.Ordinal))
        {
            return VerificationResultCodes.ReleaseIntegrityMutableArtifactReference;
        }

        if (validationError.Contains("circuit", StringComparison.Ordinal) ||
            validationError.Contains("proving-key", StringComparison.Ordinal) ||
            validationError.Contains("verifying-key", StringComparison.Ordinal))
        {
            return VerificationResultCodes.ReleaseIntegrityCircuitOrPackageHashMismatch;
        }

        if (validationError.Contains("lifecycle binding", StringComparison.Ordinal))
        {
            return VerificationResultCodes.ReleaseIntegrityLifecycleMismatch;
        }

        if (validationError.Contains("schema", StringComparison.Ordinal))
        {
            return VerificationResultCodes.ReleaseIntegrityManifestMissing;
        }

        if (validationError.Contains("evidence mode", StringComparison.Ordinal) ||
            validationError.Contains("not_for_release_integrity_claims", StringComparison.Ordinal))
        {
            return VerificationResultCodes.ReleaseIntegrityEvidenceModeNotAllowed;
        }

        return VerificationResultCodes.ReleaseIntegrityComponentHashMismatch;
    }

    private sealed record Sp08ManifestEvaluation(
        string PrimaryResultCode,
        string PrimaryIssue);
}
