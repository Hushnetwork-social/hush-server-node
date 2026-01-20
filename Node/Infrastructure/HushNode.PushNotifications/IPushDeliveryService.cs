using HushNode.Interfaces.Models;
using HushNode.PushNotifications.Models;

namespace HushNode.PushNotifications;

/// <summary>
/// Service interface for delivering push notifications to users.
/// Handles sending to all user devices and managing invalid tokens.
/// </summary>
public interface IPushDeliveryService
{
    /// <summary>
    /// Sends a push notification to all active devices for a user.
    /// Automatically handles invalid tokens by deactivating them.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="payload">The push notification payload.</param>
    /// <returns>A task representing the async operation.</returns>
    Task SendPushAsync(string userId, PushPayload payload);

    /// <summary>
    /// Sends a push notification to a specific device.
    /// </summary>
    /// <param name="deviceToken">The device token to send to.</param>
    /// <param name="payload">The push notification payload.</param>
    /// <returns>A task representing the async operation.</returns>
    /// <exception cref="Exceptions.InvalidTokenException">
    /// Thrown when the device token is invalid or expired.
    /// </exception>
    Task SendPushToDeviceAsync(DeviceToken deviceToken, PushPayload payload);
}
