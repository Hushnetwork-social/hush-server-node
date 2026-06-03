using System.Globalization;

namespace GovernanceCustomerHandoffPromoter;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var workspaceRoot = GetOption(args, "--workspace-root", "-WorkspaceRoot", "-workspace-root") ??
                WorkspaceRootFinder.Find(AppContext.BaseDirectory);
            var mode = GetOption(args, "--mode", "-Mode", "-mode");
            var sourceInput = GetOption(args, "--source-input", "-SourceInput", "-source-input");
            var outputRoot = GetOption(args, "--output-root", "-OutputRoot", "-output-root");
            var generatedAt = GetOption(args, "--generated-at", "-GeneratedAt", "-generated-at");
            var validateOnly = args.Contains("--validate-only", StringComparer.OrdinalIgnoreCase);
            var publicOnly = args.Contains("--public-only", StringComparer.OrdinalIgnoreCase) ||
                args.Contains("-PublicOnly", StringComparer.OrdinalIgnoreCase) ||
                args.Contains("-public-only", StringComparer.OrdinalIgnoreCase);
            var paths = GovernanceCustomerHandoffPromotionPaths.FromWorkspaceRoot(workspaceRoot);
            var result = new GovernanceCustomerHandoffPromotionService().Promote(new(
                paths,
                mode,
                sourceInput,
                outputRoot,
                string.IsNullOrWhiteSpace(generatedAt)
                    ? null
                    : DateTimeOffset.Parse(
                        generatedAt,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                validateOnly,
                publicOnly));

            Console.WriteLine($"Mode: {result.Mode}");
            Console.WriteLine($"Status: {result.Status}");
            Console.WriteLine($"Package root: {result.PackageRoot}");
            Console.WriteLine($"Artifacts: {result.GeneratedPackage.ArtifactCount}");
            foreach (var file in result.WrittenFiles)
            {
                Console.WriteLine($"Wrote: {file}");
            }

            foreach (var file in result.CheckedFiles)
            {
                Console.WriteLine($"Checked: {file}");
            }

            return 0;
        }
        catch (GovernanceCustomerHandoffPromotionException ex)
        {
            Console.Error.WriteLine(ex.Message);
            foreach (var detail in ex.Details)
            {
                Console.Error.WriteLine($"- {detail}");
            }

            return 1;
        }
    }

    private static string? GetOption(string[] args, params string[] names)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (names.Any(name => string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
