using System.Security.Cryptography;
using System.Text.Json;

namespace HushServerNode.Testing;

public static class ReactionArtifactReleaseValidator
{
    public static string GetDefaultManifestPath(string workspaceRoot) =>
        Path.Combine(
            Path.GetFullPath(workspaceRoot),
            "hush-memory-bank",
            "Features",
            "03_IN_PROGRESS",
            "FEAT-087-reactions-privacy-preserving-semantics",
            "approved-circuit-artifact-release.json");

    public static ReactionArtifactReleaseValidationResult ValidateFromWorkspaceRoot(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var manifestPath = GetDefaultManifestPath(workspaceRoot);
        if (!File.Exists(manifestPath))
        {
            return new ReactionArtifactReleaseValidationResult(
                IsValid: false,
                ManifestPath: manifestPath,
                Notes: "Approved circuit artifact release manifest missing");
        }

        var manifest = JsonSerializer.Deserialize<ReactionArtifactReleaseManifest>(
            File.ReadAllText(manifestPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        if (manifest is null)
        {
            return new ReactionArtifactReleaseValidationResult(
                IsValid: false,
                ManifestPath: manifestPath,
                Notes: "Approved circuit artifact release manifest could not be parsed");
        }

        if (!string.Equals(manifest.Version, "omega-v1.0.0", StringComparison.Ordinal))
        {
            return new ReactionArtifactReleaseValidationResult(
                IsValid: false,
                ManifestPath: manifestPath,
                Notes: $"Approved circuit artifact release manifest has unexpected version '{manifest.Version}'");
        }

        if (manifest.Files is null || manifest.Files.Count == 0)
        {
            return new ReactionArtifactReleaseValidationResult(
                IsValid: false,
                ManifestPath: manifestPath,
                Notes: "Approved circuit artifact release manifest contains no files");
        }

        var normalizedRoot = Path.GetFullPath(workspaceRoot);
        foreach (var file in manifest.Files)
        {
            if (string.IsNullOrWhiteSpace(file.RelativePath) || string.IsNullOrWhiteSpace(file.Sha256))
            {
                return new ReactionArtifactReleaseValidationResult(
                    IsValid: false,
                    ManifestPath: manifestPath,
                    Notes: "Approved circuit artifact release manifest contains incomplete file metadata");
            }

            var absolutePath = Path.Combine(normalizedRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolutePath))
            {
                return new ReactionArtifactReleaseValidationResult(
                    IsValid: false,
                    ManifestPath: manifestPath,
                    Notes: $"Approved artifact missing at '{file.RelativePath}'");
            }

            var actualSha256 = ComputeSha256Hex(absolutePath);
            if (!actualSha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return new ReactionArtifactReleaseValidationResult(
                    IsValid: false,
                    ManifestPath: manifestPath,
                    Notes: $"SHA-256 mismatch for '{file.RelativePath}'");
            }
        }

        return new ReactionArtifactReleaseValidationResult(
            IsValid: true,
            ManifestPath: manifestPath,
            Notes: "Approved circuit artifact release manifest and installed files match");
    }

    private static string ComputeSha256Hex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
