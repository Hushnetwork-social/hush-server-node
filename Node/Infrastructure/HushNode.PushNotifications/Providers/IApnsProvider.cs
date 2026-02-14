using HushNode.PushNotifications.Models;

namespace HushNode.PushNotifications.Providers;

/// <summary>
/// Provider interface for Apple Push Notification service (APNs) communication.
/// Abstracts APNs API calls for testability.
/// </summary>
public interface IApnsProvider
{
    /// <summary>
    /// Sends a push notification to a specific APNs device token.
    /// </summary>
    /// <param name="apnsToken">The APNs device token to send to.</param>
    /// <param name="payload">The push notification payload.</param>
    /// <returns>A task representing the async operation.</returns>
    /// <exception cref="Exceptions.InvalidTokenException">
    /// Thrown when the APNs token is invalid, expired, or unregistered.
    /// </exception>
    Task SendAsync(string apnsToken, PushPayload payload);
}
