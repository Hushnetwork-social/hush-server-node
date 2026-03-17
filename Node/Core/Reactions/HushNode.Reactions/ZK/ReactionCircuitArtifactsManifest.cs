namespace HushNode.Reactions.ZK;

/// <summary>
/// FEAT-087 approved server-side circuit artifact manifest.
/// </summary>
public static class ReactionCircuitArtifactsManifest
{
    private static readonly IReadOnlyDictionary<string, ApprovedCircuitArtifacts> ApprovedArtifactsByVersion =
        new Dictionary<string, ApprovedCircuitArtifacts>(StringComparer.Ordinal)
        {
            ["omega-v1.0.0"] = new(
                "omega-v1.0.0",
                Path.Combine("circuits", "omega-v1.0.0", "verification_key.json"),
                "FEAT-087 approved server artifact set")
        };

    public static bool IsApproved(string version) => ApprovedArtifactsByVersion.ContainsKey(version);

    public static ApprovedCircuitArtifacts GetApproved(string version)
    {
        if (!ApprovedArtifactsByVersion.TryGetValue(version, out var artifacts))
        {
            throw new InvalidOperationException(
                $"Circuit version '{version}' is not part of the approved FEAT-087 server artifact set.");
        }

        return artifacts;
    }

    public static IReadOnlyCollection<string> ListApprovedVersions() =>
        ApprovedArtifactsByVersion.Keys.ToArray();
}

public sealed record ApprovedCircuitArtifacts(
    string Version,
    string VerificationKeyRelativePath,
    string Provenance);
