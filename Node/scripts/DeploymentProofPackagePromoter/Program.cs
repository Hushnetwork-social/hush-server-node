using System.Globalization;
using DeploymentProofPackagePromoter;

try
{
    var arguments = CommandLineArguments.Parse(args);
    var workspaceRoot = CommandLineArguments.TryGetValue(arguments, "workspace-root", out var configuredWorkspaceRoot)
        ? configuredWorkspaceRoot
        : WorkspaceRootFinder.Find(Directory.GetCurrentDirectory());
    var paths = DeploymentProofPackagePromotionPaths.FromWorkspaceRoot(workspaceRoot);

    if (CommandLineArguments.TryGetValue(arguments, "source-root", out var sourceRoot))
    {
        paths = paths with { SourceRoot = sourceRoot };
    }

    if (CommandLineArguments.TryGetValue(arguments, "public-output-root", out var publicOutputRoot))
    {
        paths = paths with { PublicOutputRoot = publicOutputRoot };
    }

    if (CommandLineArguments.TryGetValue(arguments, "restricted-output-root", out var restrictedOutputRoot))
    {
        paths = paths with { RestrictedOutputRoot = restrictedOutputRoot };
    }

    var options = new DeploymentProofPackagePromotionOptions(
        paths,
        CommandLineArguments.TryGetValue(arguments, "mode", out var mode) ? mode : null,
        CommandLineArguments.TryGetValue(arguments, "component-id", out var componentId) ? componentId : null,
        CommandLineArguments.TryGetValue(arguments, "deployment-proof-id", out var deploymentProofId) ? deploymentProofId : null,
        CommandLineArguments.TryGetValue(arguments, "ceremony-id", out var ceremonyId) ? ceremonyId : null,
        CommandLineArguments.TryGetValue(arguments, "classification-input", out var classificationInput) ? classificationInput : null,
        CommandLineArguments.TryGetValue(arguments, "cd-provider", out var cdProvider) ? cdProvider : null,
        CommandLineArguments.TryGetValue(arguments, "cd-run-id", out var cdRunId) ? cdRunId : null,
        CommandLineArguments.TryGetValue(arguments, "generated-at", out var generatedAt)
            ? DateTimeOffset.Parse(generatedAt, null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
            : null,
        arguments.ContainsKey("validate-only"),
        arguments.ContainsKey("scaffold"),
        arguments.ContainsKey("capture-live-evidence"));

    var result = new DeploymentProofPackagePromotionService().Promote(options);

    Console.WriteLine(options.ValidateOnly
        ? $"Validated deployment proof package mode {result.Mode}"
        : $"Promoted deployment proof package mode {result.Mode}");
    Console.WriteLine($"Package ID: {result.PackageId}");
    Console.WriteLine($"Generated at: {result.GeneratedAt:O}");
    Console.WriteLine($"Manifest hash: {result.ManifestHash}");
    Console.WriteLine($"Archive hash: {result.ArchiveHash}");
    Console.WriteLine($"Catalog: {result.CatalogPath}");
    Console.WriteLine($"Written files: {result.WrittenFiles.Count}");
    return 0;
}
catch (DeploymentProofPackagePromotionException ex)
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
