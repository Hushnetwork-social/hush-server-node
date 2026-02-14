namespace HushNode.PushNotifications;

/// <summary>
/// Strongly-typed configuration for Apple Push Notification service (APNs).
/// Bound from the "ApnsSettings" section in ApplicationSettings.json via IOptions.
/// </summary>
public class ApnsSettings
{
    /// <summary>
    /// Whether APNs push notifications are active.
    /// Defaults to false so existing deployments are unaffected.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Apple Auth Key ID (10-character identifier from Apple Developer portal).
    /// </summary>
    public string? KeyId { get; set; }

    /// <summary>
    /// Apple Developer Team ID (10-character identifier).
    /// </summary>
    public string? TeamId { get; set; }

    /// <summary>
    /// iOS app bundle identifier (e.g., "social.hushnetwork.feeds").
    /// </summary>
    public string? BundleId { get; set; }

    /// <summary>
    /// File path to the .p8 private key from Apple Developer portal.
    /// </summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>
    /// Whether to use the APNs sandbox endpoint (true) or production endpoint (false).
    /// Defaults to true for safety to prevent accidental production pushes during development.
    /// </summary>
    public bool UseSandbox { get; set; } = true;
}
