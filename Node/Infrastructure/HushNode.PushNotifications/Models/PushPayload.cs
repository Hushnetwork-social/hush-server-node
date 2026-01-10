namespace HushNode.PushNotifications.Models;

/// <summary>
/// Represents the payload for a push notification.
/// Contains the notification content and optional navigation data.
/// </summary>
/// <param name="Title">The notification title (e.g., sender name or app name).</param>
/// <param name="Body">The notification body/message preview.</param>
/// <param name="FeedId">Optional feed ID for navigation when notification is tapped.</param>
/// <param name="Data">Optional additional key-value data for the notification.</param>
public record PushPayload(
    string Title,
    string Body,
    string? FeedId = null,
    Dictionary<string, string>? Data = null);
