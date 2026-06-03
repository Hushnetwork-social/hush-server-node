namespace GovernanceCustomerHandoffPromoter;

public static class WorkspaceRootFinder
{
    public static string Find(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Governance-Customer-Handoff")) &&
                Directory.Exists(Path.Combine(directory.FullName, "hush-server-node")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new GovernanceCustomerHandoffPromotionException(
            "Unable to locate HushNetwork workspace root.",
            ["Pass -WorkspaceRoot/--workspace-root explicitly."]);
    }
}
