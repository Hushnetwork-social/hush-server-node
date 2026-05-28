namespace ProductionLikeOperationalRunPromoter;

public static class WorkspaceRootFinder
{
    public static string Find(string startPath)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startPath));
        while (current is not null)
        {
            if (IsWorkspaceRoot(current.FullName))
            {
                return current.FullName;
            }

            var parentCandidate = Path.GetFullPath(Path.Combine(current.FullName, ".."));
            if (IsWorkspaceRoot(parentCandidate))
            {
                return parentCandidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find HushNetwork workspace root.");
    }

    private static bool IsWorkspaceRoot(string path) =>
        Directory.Exists(Path.Combine(path, "hush-memory-bank")) &&
        Directory.Exists(Path.Combine(path, "hush-server-node")) &&
        Directory.Exists(Path.Combine(path, "hush-documents"));
}
