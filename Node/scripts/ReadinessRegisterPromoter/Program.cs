using ReadinessRegisterPromoter;

var arguments = CommandLineArguments.Parse(args);
var workspaceRoot = CommandLineArguments.TryGetValue(arguments, "workspace-root", out var configuredWorkspaceRoot)
    ? configuredWorkspaceRoot
    : WorkspaceRootFinder.Find(Directory.GetCurrentDirectory());
var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(workspaceRoot);

if (CommandLineArguments.TryGetValue(arguments, "source-root", out var sourceRoot))
{
    paths = paths with { SourceRoot = sourceRoot };
}

if (CommandLineArguments.TryGetValue(arguments, "output-root", out var outputRoot))
{
    paths = paths with { OutputRoot = outputRoot };
}

var options = new ReadinessRegisterPromotionOptions(
    paths,
    CommandLineArguments.TryGetValue(arguments, "register-id", out var registerId) ? registerId : "hushvoting-readiness-register",
    CommandLineArguments.TryGetValue(arguments, "version", out var version) ? version : null,
    CommandLineArguments.TryGetValue(arguments, "publication-status", out var publicationStatus) ? publicationStatus : null,
    arguments.ContainsKey("validate-only"),
    arguments.ContainsKey("scaffold"),
    CommandLineArguments.TryGetValue(arguments, "generated-at", out var generatedAt)
        ? DateTimeOffset.Parse(
            generatedAt,
            null,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal)
        : null,
    arguments.ContainsKey("check-only"),
    arguments.ContainsKey("replace-existing"));

try
{
    var result = new ReadinessRegisterPromotionService().Promote(options);

    Console.WriteLine(options.CheckOnly
        ? $"Checked readiness register {result.RegisterVersionId}"
        : options.ValidateOnly
            ? $"Validated readiness register {result.RegisterVersionId}"
            : $"Promoted readiness register {result.RegisterVersionId}");
    Console.WriteLine($"Status: {result.Status}");
    Console.WriteLine($"Generated at: {result.GeneratedAt:O}");
    Console.WriteLine($"Total score: {result.TotalScore}/100");
    Console.WriteLine($"Strongest allowed claim: {result.StrongestAllowedClaim}");
    Console.WriteLine($"Publication status: {result.PublicationStatus}");
    Console.WriteLine($"Manifest hash: {result.ManifestHash}");
    Console.WriteLine($"Archive hash: {result.ArchiveHash}");
    Console.WriteLine($"Catalog: {result.CatalogPath}");
    Console.WriteLine($"Private artifact path: {result.VersionOutputRoot}");

    return 0;
}
catch (ReadinessRegisterPromotionException ex)
{
    Console.Error.WriteLine(ex.Message);
    foreach (var detail in ex.Details)
    {
        Console.Error.WriteLine($"- {detail}");
    }

    return 2;
}
