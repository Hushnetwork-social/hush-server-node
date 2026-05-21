using RetentionLogPrivacyProofPromoter;

try
{
    if (args.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine("RetentionLogPrivacyProofPromoter validates and generates retention/log privacy proof packages.");
        Console.WriteLine("Modes: validate_only, check_only, package.");
        Console.WriteLine("Options: --workspace-root, --output-root, --generated-at, --validate-only, --mode.");
        Console.WriteLine("Source refs: --server-source-ref, --memory-bank-source-ref, --documents-source-ref.");
        return 0;
    }

    var arguments = CommandLineArguments.Parse(args);
    var workspaceRoot = CommandLineArguments.TryGetValue(arguments, "workspace-root", out var configuredWorkspaceRoot)
        ? configuredWorkspaceRoot
        : WorkspaceRootFinder.Find(Directory.GetCurrentDirectory());
    var paths = RetentionLogPrivacyProofPromotionPaths.FromWorkspaceRoot(workspaceRoot);

    var options = new RetentionLogPrivacyProofPromotionOptions(
        paths,
        CommandLineArguments.TryGetValue(arguments, "mode", out var mode) ? mode : null,
        CommandLineArguments.TryGetValue(arguments, "generated-at", out var generatedAt)
            ? RetentionLogPrivacyProofPromotionService.ParseTimestamp(generatedAt)
            : null,
        CommandLineArguments.TryGetValue(arguments, "output-root", out var outputRoot) ? outputRoot : null,
        CommandLineArguments.TryGetValue(arguments, "server-source-ref", out var serverSourceRef) ? serverSourceRef : null,
        CommandLineArguments.TryGetValue(arguments, "memory-bank-source-ref", out var memoryBankSourceRef) ? memoryBankSourceRef : null,
        CommandLineArguments.TryGetValue(arguments, "documents-source-ref", out var documentsSourceRef) ? documentsSourceRef : null,
        arguments.ContainsKey("validate-only"));

    var result = new RetentionLogPrivacyProofPromotionService().Promote(options);
    Console.WriteLine(result.Mode == RetentionLogPrivacyProofPromotionService.ModePackage
        ? $"Generated retention/log privacy proof package {result.PackageId}"
        : $"Validated retention/log privacy proof package {result.PackageId}");
    Console.WriteLine($"Status: {result.Status}");
    Console.WriteLine($"Generated at: {result.GeneratedAt:O}");
    Console.WriteLine($"Checks: {result.CheckResult.Checks.Count}");
    Console.WriteLine($"Artifacts: {result.Artifacts.Count}");
    Console.WriteLine($"Scan findings: {result.ScanFindings.Count}");
    Console.WriteLine($"Written files: {result.WrittenFiles.Count}");
    return result.Status == "blocked" ? 2 : 0;
}
catch (RetentionLogPrivacyProofPromotionException ex)
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
