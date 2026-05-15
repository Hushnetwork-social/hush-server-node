using HushShared.Elections.Model;

namespace HushShared.Elections.Verification.Model;

internal sealed record RestrictedAnomalyIntakeManifestArtifactRecord(
    string? ArtifactSchemaId,
    string? ManifestHash,
    string? CanonicalizationId,
    string? ScopeId,
    string? PackageReadinessStatusId,
    IReadOnlyList<string>? PackageReadinessBlockerIds,
    int ThreadCount,
    int AttachmentManifestCount,
    int RedactionCount,
    int RecipientStatusCount,
    AnomalyIntakeManifest? Manifest);
