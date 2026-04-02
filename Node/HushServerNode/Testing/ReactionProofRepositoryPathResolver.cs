namespace HushServerNode.Testing;

public static class ReactionProofRepositoryPathResolver
{
    public static ReactionProofRepositoryPaths ResolveFromRuntimeBase(string runtimeBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeBaseDirectory);

        var serverAttemptedPaths = new List<string>();
        var serverRepositoryRoot = ResolveServerRepositoryRoot(runtimeBaseDirectory, serverAttemptedPaths);

        var webAttemptedPaths = new List<string>();
        var webClientRoot = ResolveWebClientRoot(serverRepositoryRoot, webAttemptedPaths);

        return new ReactionProofRepositoryPaths(serverRepositoryRoot, webClientRoot);
    }

    public static ReactionProofRepositoryPaths ResolveFromWorkspaceRoot(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var normalizedRoot = Path.GetFullPath(workspaceRoot);
        var serverRepositoryRoot = Path.Combine(normalizedRoot, "hush-server-node");
        var webClientRoot = Path.Combine(normalizedRoot, "hush-web-client");

        if (!IsServerRepositoryRoot(serverRepositoryRoot))
        {
            throw new InvalidOperationException(
                $"Workspace root '{normalizedRoot}' does not contain hush-server-node/Node/HushServerNode.sln.");
        }

        if (!IsWebClientRoot(webClientRoot))
        {
            throw new InvalidOperationException(
                $"Workspace root '{normalizedRoot}' does not contain hush-web-client/package.json and scripts/generate-reaction-proof.mjs.");
        }

        return new ReactionProofRepositoryPaths(serverRepositoryRoot, webClientRoot);
    }

    private static string ResolveServerRepositoryRoot(string runtimeBaseDirectory, List<string> attemptedPaths)
    {
        var explicitServerRoot = Environment.GetEnvironmentVariable("HUSH_SERVER_NODE_ROOT");
        if (TryResolveServerRepositoryRoot(explicitServerRoot, attemptedPaths, out var resolvedServerRoot))
        {
            return resolvedServerRoot;
        }

        var current = new DirectoryInfo(Path.GetFullPath(runtimeBaseDirectory));
        while (current is not null)
        {
            if (TryResolveServerRepositoryRoot(current.FullName, attemptedPaths, out resolvedServerRoot))
            {
                return resolvedServerRoot;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Unable to resolve hush-server-node root from the current runtime base directory. " +
            "Set HUSH_SERVER_NODE_ROOT in CI or provide a checkout layout containing Node/HushServerNode.sln. " +
            $"Attempted: {string.Join(", ", attemptedPaths.Distinct(StringComparer.OrdinalIgnoreCase))}");
    }

    private static string ResolveWebClientRoot(string serverRepositoryRoot, List<string> attemptedPaths)
    {
        var explicitWebClientRoot = Environment.GetEnvironmentVariable("HUSH_WEB_CLIENT_ROOT");
        if (TryResolveWebClientRoot(explicitWebClientRoot, attemptedPaths, out var resolvedWebClientRoot))
        {
            return resolvedWebClientRoot;
        }

        var nestedWebClientRoot = Path.Combine(serverRepositoryRoot, "hush-web-client");
        if (TryResolveWebClientRoot(nestedWebClientRoot, attemptedPaths, out resolvedWebClientRoot))
        {
            return resolvedWebClientRoot;
        }

        var siblingWebClientRoot = Path.Combine(
            Directory.GetParent(serverRepositoryRoot)?.FullName ?? serverRepositoryRoot,
            "hush-web-client");
        if (TryResolveWebClientRoot(siblingWebClientRoot, attemptedPaths, out resolvedWebClientRoot))
        {
            return resolvedWebClientRoot;
        }

        throw new InvalidOperationException(
            "Unable to resolve hush-web-client root from the current runtime base directory. " +
            "Set HUSH_WEB_CLIENT_ROOT in CI or provide a checkout layout containing package.json and scripts/generate-reaction-proof.mjs. " +
            $"Attempted: {string.Join(", ", attemptedPaths.Distinct(StringComparer.OrdinalIgnoreCase))}");
    }

    private static bool TryResolveServerRepositoryRoot(
        string? candidate,
        List<string> attemptedPaths,
        out string resolvedRoot)
    {
        resolvedRoot = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(candidate);
        attemptedPaths.Add(fullPath);

        if (!IsServerRepositoryRoot(fullPath))
        {
            return false;
        }

        resolvedRoot = fullPath;
        return true;
    }

    private static bool TryResolveWebClientRoot(
        string? candidate,
        List<string> attemptedPaths,
        out string resolvedRoot)
    {
        resolvedRoot = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(candidate);
        attemptedPaths.Add(fullPath);

        if (!IsWebClientRoot(fullPath))
        {
            return false;
        }

        resolvedRoot = fullPath;
        return true;
    }

    private static bool IsServerRepositoryRoot(string candidate)
    {
        return Directory.Exists(candidate) &&
            File.Exists(Path.Combine(candidate, "Node", "HushServerNode.sln"));
    }

    private static bool IsWebClientRoot(string candidate)
    {
        return Directory.Exists(candidate) &&
            File.Exists(Path.Combine(candidate, "package.json")) &&
            File.Exists(Path.Combine(candidate, "scripts", "generate-reaction-proof.mjs"));
    }
}
