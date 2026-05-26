using System.Globalization;

namespace PilotEvidencePackagePromoter;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var workspaceRoot = GetOption(args, "--workspace-root") ?? WorkspaceRootFinder.Find(AppContext.BaseDirectory);
            var mode = GetOption(args, "--mode");
            var sourceInput = GetOption(args, "--source-input");
            var outputRoot = GetOption(args, "--output-root");
            var generatedAt = GetOption(args, "--generated-at");
            var validateOnly = args.Contains("--validate-only", StringComparer.OrdinalIgnoreCase);
            var paths = PilotEvidencePackagePromotionPaths.FromWorkspaceRoot(workspaceRoot);

            var result = new PilotEvidencePackagePromotionService().Promote(new(
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
                validateOnly));

            Console.WriteLine($"Mode: {result.Mode}");
            Console.WriteLine($"Status: {result.Status}");
            Console.WriteLine($"Artifacts: {result.GeneratedPackage.Artifacts.Count}");
            foreach (var file in result.WrittenFiles)
            {
                Console.WriteLine(file);
            }

            return result.Status == "blocked" ? 2 : 0;
        }
        catch (PilotEvidencePackagePromotionException ex)
        {
            Console.Error.WriteLine(ex.Message);
            foreach (var detail in ex.Details)
            {
                Console.Error.WriteLine($"- {detail}");
            }

            return 1;
        }
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
