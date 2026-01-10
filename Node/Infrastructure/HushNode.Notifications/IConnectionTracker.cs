namespace HushNode.Notifications;

/// <summary>
/// Service for tracking active gRPC connections per user.
/// Uses Redis SETs to support multiple connections per user (multi-device).
/// </summary>
public interface IConnectionTracker
{
    /// <summary>
    /// Mark a user as online with a specific connection.
    /// Adds the connection ID to the user's connection set and refreshes TTL.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="connectionId">The unique connection ID (GUID).</param>
    Task MarkOnlineAsync(string userId, string connectionId);

    /// <summary>
    /// Mark a user's connection as offline.
    /// Removes the connection ID from the user's connection set.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="connectionId">The unique connection ID (GUID).</param>
    Task MarkOfflineAsync(string userId, string connectionId);

    /// <summary>
    /// Check if a user has any active connections.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>True if the user has at least one active connection.</returns>
    Task<bool> IsUserOnlineAsync(string userId);

    /// <summary>
    /// Get the total count of online users.
    /// Note: This scans Redis keys and should be used sparingly.
    /// </summary>
    /// <returns>The number of users with at least one active connection.</returns>
    Task<int> GetOnlineUserCountAsync();
}
