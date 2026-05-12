using HushShared.Elections.Model;

namespace HushNode.Elections;

public sealed record ProtocolPackagePromotionPaths(
    string WorkingSourceRoot,
    string OfficialArtifactsRoot,
    string ServerCatalogPath,
    string? WebsitePublicArtifactsRoot = null,
    string? PublicPackageRepositoryArtifactsRoot = null)
{
    public static ProtocolPackagePromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot));
        }

        var root = Path.GetFullPath(workspaceRoot);
        var publicPackageRepositoryRoot = Path.Combine(root, "protocol-omega-packages");
        return new ProtocolPackagePromotionPaths(
            Path.Combine(
                root,
                "hush-memory-bank",
                "Overview",
                "ProtocolOmega",
                "Protocol-Omega-HushVoting-v1-Artifacts"),
            Path.Combine(
                root,
                "hush-documents",
                "PrivateServer_ElectronicVoting",
                "Protocol-Omega-HushVoting-v1-Artifacts"),
            Path.Combine(
                root,
                "hush-server-node",
                "Node",
                "Core",
                "Elections",
                "HushNode.Elections",
                "ProtocolPackages",
                "ApprovedProtocolPackageCatalog.json"),
            Path.Combine(
                root,
                "hush-website",
                "public",
                "protocol-omega",
                "hushvoting-v1"),
            Directory.Exists(publicPackageRepositoryRoot)
                ? Path.Combine(publicPackageRepositoryRoot, "hushvoting-v1")
                : null);
    }
}

public sealed record ProtocolPackagePromotionOptions(
    ProtocolPackagePromotionPaths Paths,
    string PackageId,
    string? PackageVersion,
    string PublicBaseUrl,
    bool ScaffoldMissingSourceFiles,
    DateTime? GeneratedAt)
{
    public static ProtocolPackagePromotionOptions Create(
        ProtocolPackagePromotionPaths paths,
        string? packageVersion = null,
        bool scaffoldMissingSourceFiles = false,
        string packageId = "omega-hushvoting-v1",
        string publicBaseUrl = "https://www.hushnetwork.social/protocol-omega/hushvoting-v1",
        DateTime? generatedAt = null) =>
        new(
            paths,
            packageId,
            packageVersion,
            publicBaseUrl,
            scaffoldMissingSourceFiles,
            generatedAt);
}

public sealed record ProtocolPackagePromotionResult(
    ProtocolPackageManifestRecord SpecificationManifest,
    ProtocolPackageManifestRecord ProofManifest,
    ProtocolOmegaPackageReleaseManifestRecord ReleaseManifest,
    ApprovedProtocolPackageCatalogEntryRecord CatalogEntry,
    IReadOnlyList<string> MissingSourceFiles,
    IReadOnlyList<string> IncompleteSourceFiles,
    IReadOnlyList<string> WrittenFiles)
{
    public bool Succeeded => MissingSourceFiles.Count == 0;
    public bool IsComplete => IncompleteSourceFiles.Count == 0;
}

public sealed class ProtocolPackagePromotionException(
    string message,
    IReadOnlyList<string> missingSourceFiles) : InvalidOperationException(message)
{
    public IReadOnlyList<string> MissingSourceFiles { get; } = missingSourceFiles;
}
