namespace GovernedOutcomeProducer;

public static class WorkspaceRootFinder
{
    public static string Find(string startPath)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startPath));
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "hush-memory-bank")) &&
                Directory.Exists(Path.Combine(current.FullName, "hush-documents")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root containing hush-memory-bank and hush-documents.");
    }
}
