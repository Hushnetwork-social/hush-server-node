using System.Globalization;
using SecurityDependencySupportReadinessPromoter;

try
{
    if (args.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine("SecurityDependencySupportReadinessPromoter validates and generates FEAT-134 readiness evidence.");
        Console.WriteLine("Modes: validate_only, check_only, package.");
        Console.WriteLine("Roots/options: --workspace-root, --source-input, --release-id, --version, --output-root, --generated-at, --publication-status, --validate-only.");
        return 0;
    }

    var arguments = CommandLineArguments.Parse(args);
    var workspaceRoot = CommandLineArguments.TryGetValue(arguments, "workspace-root", out var configuredWorkspaceRoot)
        ? configuredWorkspaceRoot
        : WorkspaceRootFinder.Find(Directory.GetCurrentDirectory());
    var paths = SecurityDependencySupportPromotionPaths.FromWorkspaceRoot(workspaceRoot);

    var options = new SecurityDependencySupportPromotionOptions(
        paths,
        CommandLineArguments.TryGetValue(arguments, "mode", out var mode) ? mode : null,
        CommandLineArguments.TryGetValue(arguments, "source-input", out var sourceInput) ? sourceInput : null,
        CommandLineArguments.TryGetValue(arguments, "release-id", out var releaseId) ? releaseId : null,
        CommandLineArguments.TryGetValue(arguments, "version", out var version) ? version : null,
        CommandLineArguments.TryGetValue(arguments, "generated-at", out var generatedAt)
            ? DateTimeOffset.Parse(generatedAt, null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
            : null,
        CommandLineArguments.TryGetValue(arguments, "output-root", out var outputRoot) ? outputRoot : null,
        CommandLineArguments.TryGetValue(arguments, "publication-status", out var publicationStatus) ? publicationStatus : null,
        arguments.ContainsKey("validate-only"));

    var result = new SecurityDependencySupportPromotionService().Promote(options);
    Console.WriteLine(result.Mode == SecurityDependencySupportPromotionService.ModePackage
        ? $"Generated FEAT-134 security dependency support package for {result.ReleaseId} {result.Version}"
        : $"Validated FEAT-134 security dependency support package for {result.ReleaseId} {result.Version}");
    Console.WriteLine($"Status: {result.Status}");
    Console.WriteLine($"Generated at: {result.GeneratedAt:O}");
    Console.WriteLine($"Checks: {result.CheckResult.Checks.Count}");
    Console.WriteLine($"Artifacts: {result.Artifacts.Count}");
    Console.WriteLine($"Scan findings: {result.ScanFindings.Count}");
    Console.WriteLine($"Written files: {result.WrittenFiles.Count}");
    if (!string.IsNullOrWhiteSpace(result.ArchivePath))
    {
        Console.WriteLine($"Archive: {result.ArchivePath}");
    }

    return result.Status == "blocked" ? 2 : 0;
}
catch (SecurityDependencySupportPromotionException ex)
{
    Console.Error.WriteLine(ex.Message);
    foreach (var detail in ex.Details)
    {
        Console.Error.WriteLine($"- {detail}");
    }

    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
