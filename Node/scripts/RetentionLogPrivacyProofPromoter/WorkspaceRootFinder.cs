namespace RetentionLogPrivacyProofPromoter;

public static class WorkspaceRootFinder
{
    public static string Find(string startDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "hush-server-node")) &&
                Directory.Exists(Path.Combine(directory.FullName, "hush-memory-bank")) &&
                Directory.Exists(Path.Combine(directory.FullName, "hush-documents")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new RetentionLogPrivacyProofPromotionException(
            "Workspace root was not found.",
            [$"startDirectory={startDirectory}"]);
    }
}
