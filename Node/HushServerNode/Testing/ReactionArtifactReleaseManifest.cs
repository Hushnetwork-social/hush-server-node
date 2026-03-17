namespace HushServerNode.Testing;

public sealed record ReactionArtifactReleaseManifest(
    string Version,
    string Provenance,
    string TrustedSetup,
    string GeneratedBy,
    IReadOnlyList<ReactionArtifactReleaseFile> Files);

public sealed record ReactionArtifactReleaseFile(
    string RelativePath,
    string Sha256);
