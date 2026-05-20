using DeploymentProofPackagePromoter;
using OperationalEvidencePromoter;
using ReadinessRegisterPromoter;
using SecurityDependencySupportReadinessPromoter;
using VerifierCorpusPromoter;

namespace HushServerNode.Tests.Elections;

internal static class HushVotingReadinessTestArtifacts
{
    private static readonly Lazy<string> ResolvedServerNodeRoot = new(ResolveServerNodeRoot);
    private static readonly Lazy<string> SharedWorkspaceRoot = new(CreateSharedWorkspaceRoot);

    public static string ServerNodeRoot => ResolvedServerNodeRoot.Value;

    public static DeploymentProofPackagePromotionPaths CreateDeploymentProofPackagePaths() =>
        DeploymentProofPackagePromotionPaths.FromWorkspaceRoot(SharedWorkspaceRoot.Value);

    public static OperationalEvidencePromotionPaths CreateOperationalEvidencePaths() =>
        OperationalEvidencePromotionPaths.FromWorkspaceRoot(SharedWorkspaceRoot.Value);

    public static SecurityDependencySupportPromotionPaths CreateSecurityDependencySupportPaths() =>
        SecurityDependencySupportPromotionPaths.FromWorkspaceRoot(SharedWorkspaceRoot.Value);

    public static VerifierCorpusPromotionPaths CreateVerifierCorpusPaths() =>
        VerifierCorpusPromotionPaths.FromWorkspaceRoot(SharedWorkspaceRoot.Value);

    public static string CreateEmptyWorkspace(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), $"{prefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "hush-memory-bank", "Overview", "HushVotingReadiness"));
        Directory.CreateDirectory(Path.Combine(root, "hush-documents", "PrivateServer_ElectronicVoting"));
        Directory.CreateDirectory(Path.Combine(root, "hush-documents", "PrivateServer_ElectronicVoting", "Deployment-Ceremonies"));
        Directory.CreateDirectory(Path.Combine(root, "hush-documents", "PrivateServer_ElectronicVoting", "HushVoting-Readiness-Register"));
        Directory.CreateDirectory(Path.Combine(root, "hush-documents", "PrivateServer_ElectronicVoting", "Operational-Security", "FEAT-133-Operational-Evidence"));
        Directory.CreateDirectory(Path.Combine(root, "hush-documents", "PrivateServer_ElectronicVoting", "Security-Dependency-Support-Readiness"));
        Directory.CreateDirectory(Path.Combine(root, "hush-memory-bank", "Overview", "HushVotingReadiness", "Verifier-Corpus"));
        Directory.CreateDirectory(Path.Combine(root, "HushVoting-Verifier-Corpus"));
        Directory.CreateDirectory(Path.Combine(root, "hush-server-node"));
        return root;
    }

    public static void CopyReadinessRegisterSources(ReadinessRegisterPromotionPaths paths) =>
        CopyDirectory(Path.Combine(FixtureRoot, "Readiness-Register"), paths.SourceRoot);

    private static string CreateSharedWorkspaceRoot()
    {
        var root = CreateEmptyWorkspace("hush-readiness-fixtures-");
        var readinessRoot = Path.Combine(root, "hush-memory-bank", "Overview", "HushVotingReadiness");
        CopyDirectory(Path.Combine(FixtureRoot, "Deployment-Proof-Packages"), Path.Combine(readinessRoot, "Deployment-Proof-Packages"));
        CopyDirectory(Path.Combine(FixtureRoot, "Operational-Evidence"), Path.Combine(readinessRoot, "Operational-Evidence"));
        CopyDirectory(Path.Combine(FixtureRoot, "Readiness-Register"), Path.Combine(readinessRoot, "Readiness-Register"));
        CopyDirectory(Path.Combine(FixtureRoot, "Security-Dependency-Support-Readiness"), Path.Combine(readinessRoot, "Security-Dependency-Support-Readiness"));
        CopyDirectory(Path.Combine(FixtureRoot, "Verifier-Corpus"), Path.Combine(readinessRoot, "Verifier-Corpus"));
        return root;
    }

    private static string FixtureRoot
    {
        get
        {
            var outputPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "HushVotingReadiness");
            if (Directory.Exists(outputPath))
            {
                return outputPath;
            }

            var sourcePath = Path.Combine(ServerNodeRoot, "Node", "HushServerNode.Tests", "Fixtures", "HushVotingReadiness");
            if (Directory.Exists(sourcePath))
            {
                return sourcePath;
            }

            throw new DirectoryNotFoundException($"HushVoting readiness test fixture root was not found at {outputPath} or {sourcePath}.");
        }
    }

    private static string ResolveServerNodeRoot()
    {
        var configured = Environment.GetEnvironmentVariable("HUSH_SERVER_NODE_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var fullConfigured = Path.GetFullPath(configured);
            if (File.Exists(Path.Combine(fullConfigured, "Node", "HushServerNode.Tests", "HushServerNode.Tests.csproj")))
            {
                return fullConfigured;
            }
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Node", "HushServerNode.Tests", "HushServerNode.Tests.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find hush-server-node repository root for repo-local test artifacts.");
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }
}
