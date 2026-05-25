namespace LegalGovernanceBoundaryPromoter;

public static class WorkspaceRootFinder
{
    public static string Find(string startPath)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startPath));
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "hush-memory-bank")) &&
                Directory.Exists(Path.Combine(current.FullName, "hush-server-node")) &&
                Directory.Exists(Path.Combine(current.FullName, "hush-documents")))
            {
                return current.FullName;
            }

            if (Directory.Exists(Path.Combine(current.FullName, "..", "hush-memory-bank")) &&
                Directory.Exists(Path.Combine(current.FullName, "..", "hush-server-node")) &&
                Directory.Exists(Path.Combine(current.FullName, "..", "hush-documents")))
            {
                return Path.GetFullPath(Path.Combine(current.FullName, ".."));
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find HushNetwork workspace root.");
    }
}
