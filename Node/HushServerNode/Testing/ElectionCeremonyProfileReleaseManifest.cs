namespace HushServerNode.Testing;

public sealed record ElectionCeremonyProfileReleaseManifest(
    string Version,
    string Provenance,
    string GeneratedBy,
    IReadOnlyList<ElectionCeremonyProfileReleaseFile> Files);

public sealed record ElectionCeremonyProfileReleaseFile(
    string RelativePath,
    string Sha256);
