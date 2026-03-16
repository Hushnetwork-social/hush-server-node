namespace HushServerNode.Testing;

public sealed record ReactionArtifactReleaseValidationResult(
    bool IsValid,
    string ManifestPath,
    string Notes);
