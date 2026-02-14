using HushNode.Caching;
using HushNode.PushNotifications.Exceptions;
using HushNode.Interfaces.Models;
using HushNode.PushNotifications.Models;
using HushNode.PushNotifications.Providers;
using Microsoft.Extensions.Logging;

namespace HushNode.PushNotifications;

/// <summary>
/// Service for delivering push notifications to users.
/// Coordinates sending to all user devices and handles invalid token cleanup.
/// Integrates with Redis cache for faster token lookups (cache-aside pattern).
/// </summary>
public class PushDeliveryService : IPushDeliveryService
{
    private readonly IDeviceTokenStorageService _tokenStorageService;
    private readonly IPushTokenCacheService _pushTokenCacheService;
    private readonly IFcmProvider _fcmProvider;
    private readonly IApnsProvider _apnsProvider;
    private readonly ILogger<PushDeliveryService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="PushDeliveryService"/>.
    /// </summary>
    /// <param name="tokenStorageService">Service for managing device tokens.</param>
    /// <param name="pushTokenCacheService">Service for caching push tokens in Redis.</param>
    /// <param name="fcmProvider">FCM provider for sending push notifications.</param>
    /// <param name="apnsProvider">APNs provider for sending push notifications to iOS devices.</param>
    /// <param name="logger">Logger instance.</param>
    public PushDeliveryService(
        IDeviceTokenStorageService tokenStorageService,
        IPushTokenCacheService pushTokenCacheService,
        IFcmProvider fcmProvider,
        IApnsProvider apnsProvider,
        ILogger<PushDeliveryService> logger)
    {
        _tokenStorageService = tokenStorageService;
        _pushTokenCacheService = pushTokenCacheService;
        _fcmProvider = fcmProvider;
        _apnsProvider = apnsProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SendPushAsync(string userId, PushPayload payload)
    {
        // Cache-aside pattern: try cache first, fallback to database on miss
        var tokenList = await GetTokensWithCacheAsync(userId);

        if (tokenList.Count == 0)
        {
            _logger.LogDebug(
                "No active device tokens found for user {UserId}, skipping push notification",
                TruncateUserId(userId));
            return;
        }

        _logger.LogDebug(
            "Sending push notification to {DeviceCount} devices for user {UserId}",
            tokenList.Count,
            TruncateUserId(userId));

        // Send to all devices in parallel
        var sendTasks = tokenList.Select(token => SendToDeviceWithErrorHandling(token, payload));
        await Task.WhenAll(sendTasks);

        _logger.LogDebug(
            "Completed push notification delivery to {DeviceCount} devices for user {UserId}",
            tokenList.Count,
            TruncateUserId(userId));
    }

    /// <summary>
    /// Gets device tokens using cache-aside pattern.
    /// Checks cache first, falls back to database on miss, then populates cache.
    /// </summary>
    private async Task<List<DeviceToken>> GetTokensWithCacheAsync(string userId)
    {
        try
        {
            // Check cache first
            var cachedTokens = await _pushTokenCacheService.GetTokensAsync(userId);

            if (cachedTokens != null)
            {
                // Cache hit
                return cachedTokens.ToList();
            }

            // Cache miss - query database
            var dbTokens = await _tokenStorageService.GetActiveTokensForUserAsync(userId);
            var tokenList = dbTokens.ToList();

            // Populate cache (non-blocking on failure)
            await TryPopulateCacheAsync(userId, tokenList);

            return tokenList;
        }
        catch (Exception ex)
        {
            // Redis unavailable - fallback to database
            _logger.LogWarning(ex,
                "Cache error for user {UserId}, falling back to database",
                TruncateUserId(userId));

            var dbTokens = await _tokenStorageService.GetActiveTokensForUserAsync(userId);
            return dbTokens.ToList();
        }
    }

    /// <summary>
    /// Attempts to populate the cache with tokens. Cache failures are non-blocking.
    /// </summary>
    private async Task TryPopulateCacheAsync(string userId, List<DeviceToken> tokens)
    {
        try
        {
            await _pushTokenCacheService.SetTokensAsync(userId, tokens);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to populate cache for user {UserId}",
                TruncateUserId(userId));
        }
    }

    /// <inheritdoc />
    public async Task SendPushToDeviceAsync(DeviceToken deviceToken, PushPayload payload)
    {
        try
        {
            switch (deviceToken.Platform)
            {
                case PushPlatform.Android:
                    await _fcmProvider.SendAsync(deviceToken.Token, payload);
                    break;

                case PushPlatform.iOS:
                    await _apnsProvider.SendAsync(deviceToken.Token, payload);
                    break;

                default:
                    _logger.LogDebug(
                        "Skipping push notification for unsupported platform {Platform}",
                        deviceToken.Platform);
                    return;
            }
        }
        catch (InvalidTokenException ex)
        {
            _logger.LogInformation(
                "Device token is invalid for user {UserId}, deactivating. Reason: {Reason}",
                TruncateUserId(deviceToken.UserId),
                ex.Message);

            // Deactivate the invalid token
            await _tokenStorageService.UnregisterTokenAsync(deviceToken.UserId, deviceToken.Token);

            // Re-throw so caller knows about the invalid token
            throw;
        }
    }

    /// <summary>
    /// Sends push to a device and handles InvalidTokenException by deactivating the token.
    /// Updates LastUsedAt on successful delivery.
    /// Other exceptions are logged but not rethrown to prevent cascade failures.
    /// </summary>
    private async Task SendToDeviceWithErrorHandling(DeviceToken deviceToken, PushPayload payload)
    {
        try
        {
            await SendPushToDeviceAsync(deviceToken, payload);

            // Successful delivery - update LastUsedAt
            await UpdateLastUsedAtAsync(deviceToken);
        }
        catch (InvalidTokenException)
        {
            // Already handled in SendPushToDeviceAsync (token deactivated)
            // Don't rethrow - other devices should still receive their notifications
        }
        catch (Exception ex)
        {
            // Log other errors but don't propagate - don't block other device sends
            _logger.LogError(
                ex,
                "Unexpected error sending push to device for user {UserId}",
                TruncateUserId(deviceToken.UserId));
        }
    }

    /// <summary>
    /// Updates LastUsedAt timestamp in both cache and database after successful push delivery.
    /// Cache update is non-blocking.
    /// </summary>
    private async Task UpdateLastUsedAtAsync(DeviceToken deviceToken)
    {
        try
        {
            // Update the token's LastUsedAt
            deviceToken.LastUsedAt = DateTime.UtcNow;

            // Update cache (non-blocking on failure)
            await TryUpdateCacheLastUsedAtAsync(deviceToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to update LastUsedAt for device token {TokenId}",
                deviceToken.Id);
        }
    }

    /// <summary>
    /// Attempts to update LastUsedAt in the cache. Cache failures are non-blocking.
    /// </summary>
    private async Task TryUpdateCacheLastUsedAtAsync(DeviceToken deviceToken)
    {
        try
        {
            await _pushTokenCacheService.AddOrUpdateTokenAsync(deviceToken.UserId, deviceToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to update cache LastUsedAt for user {UserId}",
                TruncateUserId(deviceToken.UserId));
        }
    }

    /// <summary>
    /// Truncates user ID for logging (privacy - don't log full addresses).
    /// </summary>
    private static string TruncateUserId(string userId)
    {
        if (string.IsNullOrEmpty(userId) || userId.Length <= 16)
            return "[hidden]";

        return $"{userId[..8]}...{userId[^8..]}";
    }
}
