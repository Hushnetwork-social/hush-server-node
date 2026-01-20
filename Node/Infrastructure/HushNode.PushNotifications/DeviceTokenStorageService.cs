using Microsoft.Extensions.Logging;
using Olimpo.EntityFramework.Persistency;
using HushNode.Caching;
using HushNode.Interfaces.Models;

namespace HushNode.PushNotifications;

/// <summary>
/// Storage service implementation for device token management.
/// Uses Unit of Work pattern for transaction handling.
/// Integrates with Redis cache for faster push token lookups.
/// </summary>
public class DeviceTokenStorageService(
    IUnitOfWorkProvider<PushNotificationsDbContext> unitOfWorkProvider,
    IPushTokenCacheService pushTokenCacheService,
    ILogger<DeviceTokenStorageService> logger) : IDeviceTokenStorageService
{
    private readonly IUnitOfWorkProvider<PushNotificationsDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly IPushTokenCacheService _pushTokenCacheService = pushTokenCacheService;
    private readonly ILogger<DeviceTokenStorageService> _logger = logger;

    public async Task<bool> RegisterTokenAsync(string userId, PushPlatform platform, string token, string? deviceName)
    {
        try
        {
            using var unitOfWork = _unitOfWorkProvider.CreateWritable();
            var repository = unitOfWork.GetRepository<IDeviceTokenRepository>();

            var existingToken = await repository.GetByTokenAsync(token);
            string? previousOwnerId = null;
            DeviceToken tokenToCache;

            if (existingToken != null)
            {
                // Token exists - track previous owner for cache invalidation
                previousOwnerId = existingToken.UserId != userId ? existingToken.UserId : null;

                // Update token (upsert behavior)
                existingToken.UserId = userId;
                existingToken.Platform = platform;
                existingToken.DeviceName = deviceName;
                existingToken.LastUsedAt = DateTime.UtcNow;
                existingToken.IsActive = true;

                await repository.UpdateAsync(existingToken);
                tokenToCache = existingToken;

                _logger.LogDebug(
                    "Updated existing device token for user {UserId}, platform {Platform}",
                    userId, platform);
            }
            else
            {
                // New token - create it
                var newToken = new DeviceToken
                {
                    Id = Guid.NewGuid().ToString(),
                    UserId = userId,
                    Platform = platform,
                    Token = token,
                    DeviceName = deviceName,
                    CreatedAt = DateTime.UtcNow,
                    LastUsedAt = DateTime.UtcNow,
                    IsActive = true
                };

                await repository.AddAsync(newToken);
                tokenToCache = newToken;

                _logger.LogDebug(
                    "Registered new device token for user {UserId}, platform {Platform}",
                    userId, platform);
            }

            await unitOfWork.CommitAsync();

            // Update cache after successful PostgreSQL commit (write-through)
            // Cache failures are non-blocking - PostgreSQL is the source of truth
            await UpdateCacheAfterRegisterAsync(userId, previousOwnerId, tokenToCache);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error registering device token for user {UserId}",
                userId);
            return false;
        }
    }

    public async Task<bool> UnregisterTokenAsync(string userId, string token)
    {
        try
        {
            using var unitOfWork = _unitOfWorkProvider.CreateWritable();
            var repository = unitOfWork.GetRepository<IDeviceTokenRepository>();

            var existingToken = await repository.GetByTokenAsync(token);

            if (existingToken == null)
            {
                _logger.LogDebug(
                    "Token not found for unregister: user {UserId}",
                    userId);
                return true; // Token doesn't exist, consider it "unregistered"
            }

            // Verify the token belongs to this user
            if (existingToken.UserId != userId)
            {
                _logger.LogWarning(
                    "Attempted to unregister token belonging to different user: {TokenUserId} vs {RequestUserId}",
                    existingToken.UserId, userId);
                return false;
            }

            // Store token ID before deactivation for cache removal
            var tokenId = existingToken.Id;

            await repository.DeactivateTokenAsync(token);
            await unitOfWork.CommitAsync();

            // Remove from cache after successful PostgreSQL commit
            // Cache failures are non-blocking
            await TryRemoveTokenFromCacheAsync(userId, tokenId);

            _logger.LogDebug(
                "Unregistered device token for user {UserId}",
                userId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error unregistering device token for user {UserId}",
                userId);
            return false;
        }
    }

    public async Task<IEnumerable<DeviceToken>> GetActiveTokensForUserAsync(string userId)
    {
        try
        {
            using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
            var repository = unitOfWork.GetRepository<IDeviceTokenRepository>();

            return await repository.GetActiveTokensForUserAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error getting active tokens for user {UserId}",
                userId);
            return [];
        }
    }

    public async Task<int> DeactivateStaleTokensAsync(DateTime threshold)
    {
        try
        {
            using var unitOfWork = _unitOfWorkProvider.CreateWritable();
            var repository = unitOfWork.GetRepository<IDeviceTokenRepository>();

            var staleTokens = await repository.GetStaleTokensAsync(threshold);
            var staleTokensList = staleTokens.ToList();
            var count = 0;

            foreach (var token in staleTokensList)
            {
                await repository.DeactivateTokenAsync(token.Token);
                count++;
            }

            await unitOfWork.CommitAsync();

            // Remove deactivated tokens from cache (non-blocking)
            foreach (var token in staleTokensList)
            {
                await TryRemoveTokenFromCacheAsync(token.UserId, token.Id);
            }

            _logger.LogInformation(
                "Deactivated {Count} stale device tokens (threshold: {Threshold})",
                count, threshold);

            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error deactivating stale tokens");
            return 0;
        }
    }

    /// <summary>
    /// Updates cache after a token registration. Cache failures are non-blocking.
    /// </summary>
    private async Task UpdateCacheAfterRegisterAsync(string userId, string? previousOwnerId, DeviceToken token)
    {
        try
        {
            // If token was reassigned, remove from previous owner's cache
            if (previousOwnerId != null)
            {
                await _pushTokenCacheService.RemoveTokenAsync(previousOwnerId, token.Id);
            }

            // Add/update token in new owner's cache
            await _pushTokenCacheService.AddOrUpdateTokenAsync(userId, token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to update cache after token registration for user {UserId}",
                userId);
        }
    }

    /// <summary>
    /// Removes a token from cache. Cache failures are non-blocking.
    /// </summary>
    private async Task TryRemoveTokenFromCacheAsync(string userId, string tokenId)
    {
        try
        {
            await _pushTokenCacheService.RemoveTokenAsync(userId, tokenId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to remove token from cache for user {UserId}",
                userId);
        }
    }
}
