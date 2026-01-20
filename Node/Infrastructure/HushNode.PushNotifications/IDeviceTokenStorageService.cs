using HushNode.Interfaces.Models;

namespace HushNode.PushNotifications;

/// <summary>
/// Storage service interface for device token management operations.
/// Provides transaction-aware operations for device tokens.
/// </summary>
public interface IDeviceTokenStorageService
{
    /// <summary>
    /// Registers a device token for a user.
    /// If the token already exists, updates the existing record.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="platform">The device platform (Android, iOS, Web).</param>
    /// <param name="token">The FCM device token.</param>
    /// <param name="deviceName">Optional device name.</param>
    /// <returns>True if successful, false otherwise.</returns>
    Task<bool> RegisterTokenAsync(string userId, PushPlatform platform, string token, string? deviceName);

    /// <summary>
    /// Unregisters (deactivates) a device token.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="token">The FCM device token to unregister.</param>
    /// <returns>True if successful, false otherwise.</returns>
    Task<bool> UnregisterTokenAsync(string userId, string token);

    /// <summary>
    /// Gets all active device tokens for a user.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <returns>Collection of active device tokens.</returns>
    Task<IEnumerable<DeviceToken>> GetActiveTokensForUserAsync(string userId);

    /// <summary>
    /// Deactivates stale tokens that haven't been used since the threshold date.
    /// </summary>
    /// <param name="threshold">Tokens not used since this date will be deactivated.</param>
    /// <returns>Number of tokens deactivated.</returns>
    Task<int> DeactivateStaleTokensAsync(DateTime threshold);
}
