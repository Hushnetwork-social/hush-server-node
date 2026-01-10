using Olimpo.EntityFramework.Persistency;
using HushNode.PushNotifications.Models;

namespace HushNode.PushNotifications;

/// <summary>
/// Repository interface for device token data access operations.
/// </summary>
public interface IDeviceTokenRepository : IRepository
{
    /// <summary>
    /// Adds a new device token to the context.
    /// </summary>
    Task AddAsync(DeviceToken token);

    /// <summary>
    /// Gets all active tokens for a user.
    /// </summary>
    Task<IEnumerable<DeviceToken>> GetActiveTokensForUserAsync(string userId);

    /// <summary>
    /// Gets a device token by its token value.
    /// </summary>
    Task<DeviceToken?> GetByTokenAsync(string token);

    /// <summary>
    /// Updates the last used timestamp for a token.
    /// </summary>
    Task UpdateLastUsedAsync(string token, DateTime lastUsed);

    /// <summary>
    /// Deactivates a token (sets IsActive to false).
    /// </summary>
    Task DeactivateTokenAsync(string token);

    /// <summary>
    /// Gets stale tokens (inactive for longer than the threshold).
    /// </summary>
    Task<IEnumerable<DeviceToken>> GetStaleTokensAsync(DateTime threshold);

    /// <summary>
    /// Updates an existing device token.
    /// </summary>
    Task UpdateAsync(DeviceToken token);
}
