using System.Security.Cryptography;

namespace HushIdentityCompatibilityConformance.Tests;

/// <summary>
/// Locates the canonical HushVoting corpus for local/CI test runs.
/// Priority: HUSH_CONFORMANCE_CORPUS env var, then sibling-repository probes
/// (hush-voting-web-client is the canonical corpus owner). The corpus is a
/// runtime test input only — normal server builds never require it.
/// </summary>
public static class CorpusLocator
{
    public static string? Find()
    {
        var env = Environment.GetEnvironmentVariable("HUSH_CONFORMANCE_CORPUS");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) return Path.GetFullPath(env);

        // Probe upward from the test assembly for the workspace root, then try
        // the sibling client repository checkout.
        var dir = AppContext.BaseDirectory;
        for (var current = new DirectoryInfo(dir); current is not null; current = current.Parent)
        {
            var serverRoot = current.FullName;
            var candidates = new[]
            {
                Path.Combine(serverRoot, "hush-voting-web-client", "conformance", "identity", "v1"),
                Path.Combine(serverRoot, "..", "hush-voting-web-client", "conformance", "identity", "v1"),
                Path.Combine(serverRoot, "hush-voting-web-client", "..", "hush-voting-web-client", "conformance", "identity", "v1"),
            };
            foreach (var c in candidates)
            {
                var full = Path.GetFullPath(c);
                if (Directory.Exists(full) && File.Exists(Path.Combine(full, "manifest.json")))
                {
                    return full;
                }
            }
            if (Directory.Exists(Path.Combine(serverRoot, ".git")) && Directory.Exists(Path.Combine(serverRoot, "hush-voting-web-client")))
            {
                break;
            }
        }
        return null;
    }

    /// <summary>SHA-256 of the corpus manifest (the pinned CI/release digest).</summary>
    public static string ManifestDigest(string corpusRoot)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(corpusRoot, "manifest.json")))).ToLowerInvariant();
    }
}
