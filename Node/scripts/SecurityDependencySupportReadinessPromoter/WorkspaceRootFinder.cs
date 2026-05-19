namespace SecurityDependencySupportReadinessPromoter;

public static class WorkspaceRootFinder
{
    public static string Find(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "hush-memory-bank")) &&
                Directory.Exists(Path.Combine(current.FullName, "hush-documents")) &&
                Directory.Exists(Path.Combine(current.FullName, "hush-server-node")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root. Pass --workspace-root <path>.");
    }
}
