namespace HushServerNode.Testing;

public sealed record ElectionCeremonyProfileReleaseValidationResult(
    bool IsValid,
    string ManifestPath,
    string Notes);
