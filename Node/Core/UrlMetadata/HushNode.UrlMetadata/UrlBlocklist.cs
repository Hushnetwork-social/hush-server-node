namespace HushNode.UrlMetadata;

/// <summary>
/// Checks URLs against a blocklist of known malicious or unwanted domains.
/// </summary>
public interface IUrlBlocklist
{
    /// <summary>
    /// Checks if a URL is blocked.
    /// </summary>
    /// <param name="url">The URL to check.</param>
    /// <returns>True if the URL is blocked, false otherwise.</returns>
    bool IsBlocked(string url);
}

/// <summary>
/// Implementation of URL blocklist with hardcoded domain list.
/// The blocklist is updated with deployments.
/// </summary>
public class UrlBlocklist : IUrlBlocklist
{
    /// <summary>
    /// Hardcoded list of blocked domains.
    /// This list should be reviewed and updated regularly by security team.
    /// </summary>
    private static readonly HashSet<string> BlockedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        // Placeholder entries - to be finalized during security review
        "malware-distribution.example",
        "phishing-site.example",
        "malicious-redirects.example",
    };

    /// <inheritdoc />
    public bool IsBlocked(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        try
        {
            var uri = new Uri(url);
            var host = uri.Host.ToLowerInvariant();

            // Direct domain match
            if (BlockedDomains.Contains(host))
                return true;

            // Check if the host is a subdomain of a blocked domain
            return IsSubdomainOfBlockedDomain(host);
        }
        catch (UriFormatException)
        {
            // Invalid URL format - not blocked, will fail during fetch
            return false;
        }
    }

    private static bool IsSubdomainOfBlockedDomain(string host)
    {
        foreach (var blockedDomain in BlockedDomains)
        {
            // Check if host ends with .blockedDomain
            var subdomainPattern = "." + blockedDomain.ToLowerInvariant();
            if (host.EndsWith(subdomainPattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
