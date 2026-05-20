namespace VerifierCorpusPromoter;

public static class WorkspaceRootFinder
{
    public static string Find(string startDirectory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "hush-memory-bank")) &&
                Directory.Exists(Path.Combine(current.FullName, "hush-server-node")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate workspace root containing hush-memory-bank and hush-server-node.");
    }
}
