namespace ProductionRolloutReadinessPromoter;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var workspaceRoot = GetOption(args, "--workspace-root") ?? WorkspaceRootFinder.Find(AppContext.BaseDirectory);
            var sourceInput = GetOption(args, "--source-input");
            var paths = ProductionRolloutReadinessPromotionPaths.FromWorkspaceRoot(workspaceRoot);

            var schemaErrors = ProductionRolloutReadinessContracts.ValidateSchemaSet(paths.SchemasRoot);
            if (schemaErrors.Count > 0)
            {
                throw new ProductionRolloutReadinessPromotionException(
                    "FEAT-148 production rollout schema validation failed.",
                    schemaErrors);
            }

            var source = ProductionRolloutReadinessContracts.LoadSource(paths, sourceInput);
            var sourceErrors = ProductionRolloutReadinessContracts.ValidateSource(source);
            if (sourceErrors.Count > 0)
            {
                throw new ProductionRolloutReadinessPromotionException(
                    "FEAT-148 production rollout source validation failed.",
                    sourceErrors);
            }

            var evaluation = ProductionRolloutReadinessGateChecker.Evaluate(source);
            Console.WriteLine($"Status: {evaluation.Status}");
            Console.WriteLine($"Production blocker: {evaluation.ProductionDecision.Severity}/{evaluation.ProductionDecision.Status}");
            Console.WriteLine($"Public/state blocker: {evaluation.PublicStateDecision.Severity}/{evaluation.PublicStateDecision.Status}");
            foreach (var blocker in evaluation.Blockers)
            {
                Console.WriteLine($"Blocker: {blocker}");
            }

            foreach (var limitation in evaluation.Limitations)
            {
                Console.WriteLine($"Limitation: {limitation}");
            }

            return evaluation.Status == "blocked" ? 2 : 0;
        }
        catch (ProductionRolloutReadinessPromotionException ex)
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
