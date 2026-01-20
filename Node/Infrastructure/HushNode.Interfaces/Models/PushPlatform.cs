namespace HushNode.Interfaces.Models;

/// <summary>
/// Represents the platform type for push notifications.
/// </summary>
public enum PushPlatform
{
    /// <summary>
    /// Unknown or unspecified platform (default/invalid).
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Android devices using Firebase Cloud Messaging.
    /// </summary>
    Android = 1,

    /// <summary>
    /// iOS devices using Apple Push Notification Service.
    /// </summary>
    iOS = 2,

    /// <summary>
    /// Web browsers using Web Push Protocol.
    /// </summary>
    Web = 3
}
