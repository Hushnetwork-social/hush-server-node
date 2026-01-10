using HushNode.PushNotifications.Models;

namespace HushNode.PushNotifications.Providers;

/// <summary>
/// Provider interface for Firebase Cloud Messaging (FCM) communication.
/// Abstracts FCM API calls for testability.
/// </summary>
public interface IFcmProvider
{
    /// <summary>
    /// Sends a push notification to a specific FCM device token.
    /// </summary>
    /// <param name="fcmToken">The FCM device token to send to.</param>
    /// <param name="payload">The push notification payload.</param>
    /// <returns>A task representing the async operation.</returns>
    /// <exception cref="Exceptions.InvalidTokenException">
    /// Thrown when the FCM token is invalid, expired, or unregistered.
    /// </exception>
    Task SendAsync(string fcmToken, PushPayload payload);
}
