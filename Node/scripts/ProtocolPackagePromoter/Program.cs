using HushNode.Elections;

var arguments = ParseArguments(args);
var workspaceRoot = TryGetArgumentValue(arguments, "workspace-root", out var configuredWorkspaceRoot)
    ? configuredWorkspaceRoot
    : FindWorkspaceRoot(Directory.GetCurrentDirectory());
var scaffold = arguments.ContainsKey("scaffold");
var packageId = TryGetArgumentValue(arguments, "package-id", out var configuredPackageId)
    ? configuredPackageId
    : "omega-hushvoting-v1";
var packageVersion = TryGetArgumentValue(arguments, "version", out var configuredPackageVersion)
    ? configuredPackageVersion
    : null;
var publicBaseUrl = TryGetArgumentValue(arguments, "public-base-url", out var configuredPublicBaseUrl)
    ? configuredPublicBaseUrl
    : "https://www.hushnetwork.social/protocol-omega/hushvoting-v1";
var generatedAt = TryGetArgumentValue(arguments, "generated-at", out var configuredGeneratedAt)
    ? DateTime.Parse(configuredGeneratedAt, null, System.Globalization.DateTimeStyles.AdjustToUniversal)
    : (DateTime?)null;

try
{
    var paths = ProtocolPackagePromotionPaths.FromWorkspaceRoot(workspaceRoot);
    var options = ProtocolPackagePromotionOptions.Create(
        paths,
        packageVersion,
        scaffold,
        packageId,
        publicBaseUrl,
        generatedAt);
    var result = new ProtocolPackagePromotionService().Promote(options);

    Console.WriteLine($"Promoted Protocol Omega package {result.ReleaseManifest.PackageVersion}");
    Console.WriteLine($"Approval status: {result.ReleaseManifest.ApprovalStatus}");
    Console.WriteLine($"Generated at: {result.ReleaseManifest.ReleasedAt:O}");
    Console.WriteLine($"Incomplete files: {result.IncompleteSourceFiles.Count}");
    Console.WriteLine($"Specification hash: {result.SpecificationManifest.PackageHash}");
    Console.WriteLine($"Proof hash: {result.ProofManifest.PackageHash}");
    Console.WriteLine($"Release manifest hash: {result.ReleaseManifest.ReleaseManifestHash}");
    Console.WriteLine($"Catalog: {paths.ServerCatalogPath}");
    Console.WriteLine($"Official artifacts: {Path.Combine(paths.OfficialArtifactsRoot, result.ReleaseManifest.PackageVersion)}");
    if (!string.IsNullOrWhiteSpace(paths.WebsitePublicArtifactsRoot))
    {
        Console.WriteLine($"Website artifacts: {Path.Combine(paths.WebsitePublicArtifactsRoot, result.ReleaseManifest.PackageVersion)}");
    }
    if (!string.IsNullOrWhiteSpace(paths.PublicPackageRepositoryArtifactsRoot))
    {
        Console.WriteLine($"Public package repository artifacts: {Path.Combine(paths.PublicPackageRepositoryArtifactsRoot, result.ReleaseManifest.PackageVersion)}");
    }

    return 0;
}
catch (ProtocolPackagePromotionException ex)
{
    Console.Error.WriteLine(ex.Message);
    foreach (var missingSourceFile in ex.MissingSourceFiles)
    {
        Console.Error.WriteLine($"- {missingSourceFile}");
    }

    return 2;
}

static Dictionary<string, string?> ParseArguments(string[] args)
{
    var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    for (var index = 0; index < args.Length; index++)
    {
        var arg = args[index];
        if (!arg.StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unexpected argument: {arg}");
        }

        var key = arg[2..];
        var hasValue = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal);
        result[key] = hasValue ? args[++index] : null;
    }

    return result;
}

static bool TryGetArgumentValue(
    Dictionary<string, string?> arguments,
    string name,
    [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
{
    if (arguments.TryGetValue(name, out value) && !string.IsNullOrWhiteSpace(value))
    {
        return true;
    }

    value = null;
    return false;
}

static string FindWorkspaceRoot(string startDirectory)
{
    var current = new DirectoryInfo(startDirectory);
    while (current is not null)
    {
        if (Directory.Exists(Path.Combine(current.FullName, "hush-memory-bank")) &&
            Directory.Exists(Path.Combine(current.FullName, "hush-documents")) &&
            Directory.Exists(Path.Combine(current.FullName, "hush-server-node")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new DirectoryNotFoundException(
        "Could not find workspace root. Pass --workspace-root <path>.");
}
