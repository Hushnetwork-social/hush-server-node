using System.Globalization;
using OperationalEvidencePromoter;

try
{
    if (args.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine("OperationalEvidencePromoter validates and generates FEAT-133 operational evidence.");
        Console.WriteLine("Modes: validate_only, check_only, rehearsal_package.");
        Console.WriteLine("Roots: --workspace-root, --source-root, --restricted-source-root, --package-output-root, --restricted-output-root.");
        Console.WriteLine("Options: --run-id, --generated-at, --validate-only, --allow-live-capture.");
        return 0;
    }

    var arguments = CommandLineArguments.Parse(args);
    var workspaceRoot = CommandLineArguments.TryGetValue(arguments, "workspace-root", out var configuredWorkspaceRoot)
        ? configuredWorkspaceRoot
        : WorkspaceRootFinder.Find(Directory.GetCurrentDirectory());
    var paths = OperationalEvidencePromotionPaths.FromWorkspaceRoot(workspaceRoot);

    if (CommandLineArguments.TryGetValue(arguments, "source-root", out var sourceRoot))
    {
        paths = paths with { SourceRoot = sourceRoot };
    }

    if (CommandLineArguments.TryGetValue(arguments, "restricted-source-root", out var restrictedSourceRoot))
    {
        paths = paths with { RestrictedTemplateRoot = restrictedSourceRoot };
    }

    var options = new OperationalEvidencePromotionOptions(
        paths,
        CommandLineArguments.TryGetValue(arguments, "mode", out var mode) ? mode : null,
        CommandLineArguments.TryGetValue(arguments, "run-id", out var runId) ? runId : null,
        CommandLineArguments.TryGetValue(arguments, "generated-at", out var generatedAt)
            ? DateTimeOffset.Parse(generatedAt, null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
            : null,
        CommandLineArguments.TryGetValue(arguments, "package-output-root", out var packageOutputRoot) ? packageOutputRoot : null,
        CommandLineArguments.TryGetValue(arguments, "restricted-output-root", out var restrictedOutputRoot) ? restrictedOutputRoot : null,
        arguments.ContainsKey("validate-only"),
        arguments.ContainsKey("allow-live-capture"));

    var result = new OperationalEvidencePromotionService().Promote(options);
    Console.WriteLine(result.Mode == OperationalEvidencePromotionService.ModeRehearsalPackage
        ? $"Generated operational evidence package for {result.RunId}"
        : $"Validated operational evidence for {result.RunId}");
    Console.WriteLine($"Status: {result.Status}");
    Console.WriteLine($"Generated at: {result.GeneratedAt:O}");
    Console.WriteLine($"Checks: {result.CheckResult.Checks.Count}");
    Console.WriteLine($"Artifacts: {result.Artifacts.Count}");
    Console.WriteLine($"Scan findings: {result.ScanFindings.Count}");
    Console.WriteLine($"Written files: {result.WrittenFiles.Count}");
    return result.Status == "blocked" ? 2 : 0;
}
catch (OperationalEvidencePromotionException ex)
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
