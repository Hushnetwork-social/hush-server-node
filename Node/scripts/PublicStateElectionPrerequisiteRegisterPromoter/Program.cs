namespace PublicStateElectionPrerequisiteRegisterPromoter;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var workspaceRoot = GetOption(args, "--workspace-root") ?? WorkspaceRootFinder.Find(AppContext.BaseDirectory);
            var sourceInput = GetOption(args, "--source-input");
            var paths = PublicStateElectionPrerequisitePromotionPaths.FromWorkspaceRoot(workspaceRoot);
            var source = PublicStateElectionPrerequisiteContracts.LoadSource(paths, sourceInput);
            var schemaErrors = PublicStateElectionPrerequisiteContracts.ValidateSchemaSet(paths.SchemasRoot);
            if (schemaErrors.Count > 0)
            {
                throw new PublicStateElectionPrerequisitePromotionException(
                    "Public/state prerequisite schema validation failed.",
                    schemaErrors);
            }

            var evaluation = PublicStateElectionPrerequisiteGateChecker.Evaluate(source);
            Console.WriteLine($"Status: {evaluation.Status}");
            Console.WriteLine($"Public/state blocker: {evaluation.PublicStateDecision.Severity}/{evaluation.PublicStateDecision.Status}");
            Console.WriteLine($"Blockers: {evaluation.Blockers.Count}");
            return 0;
        }
        catch (PublicStateElectionPrerequisitePromotionException ex)
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
