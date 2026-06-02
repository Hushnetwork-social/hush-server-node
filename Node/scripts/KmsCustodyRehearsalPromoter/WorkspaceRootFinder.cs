namespace KmsCustodyRehearsalPromoter;

public static class WorkspaceRootFinder
{
    public static string Find(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "hush-server-node")) &&
                Directory.Exists(Path.Combine(current.FullName, "Kms-Custody-Rehearsal")))
            {
                return current.FullName;
            }

            if (File.Exists(Path.Combine(current.FullName, "Node", "HushServerNode.sln")))
            {
                var parent = current.Parent;
                if (parent is not null)
                {
                    return parent.FullName;
                }
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find HushNetworkOrg workspace root.");
    }
}
