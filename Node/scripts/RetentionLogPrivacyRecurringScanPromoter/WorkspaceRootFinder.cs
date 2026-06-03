namespace RetentionLogPrivacyRecurringScanPromoter;

public static class WorkspaceRootFinder
{
    public static string Find(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Retention-Log-Privacy-Scans")) &&
                Directory.Exists(Path.Combine(directory.FullName, "hush-server-node")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new RetentionLogPrivacyRecurringScanPromotionException(
            "Unable to locate HushNetwork workspace root.",
            ["Pass -WorkspaceRoot/--workspace-root explicitly."]);
    }
}
