using DeploymentProofPackagePromoter;

try
{
    var workspaceRoot = args.Length >= 2 && string.Equals(args[0], "--workspace-root", StringComparison.Ordinal)
        ? args[1]
        : WorkspaceRootFinder.Find(Directory.GetCurrentDirectory());
    var paths = DeploymentProofPackagePromotionPaths.FromWorkspaceRoot(workspaceRoot);
    var errors = DeploymentProofPackageContracts.ValidateSchemaSet(paths.SchemasRoot);

    if (errors.Count > 0)
    {
        Console.Error.WriteLine("Deployment proof package contract validation failed.");
        foreach (var error in errors)
        {
            Console.Error.WriteLine($"- {error}");
        }

        return 2;
    }

    Console.WriteLine("Deployment proof package contracts are available.");
    Console.WriteLine($"Source root: {paths.SourceRoot}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
