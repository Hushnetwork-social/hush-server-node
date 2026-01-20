using Microsoft.Extensions.Logging;
using Olimpo.EntityFramework.Persistency;
using HushNode.Interfaces.Models;

namespace HushNode.PushNotifications;

/// <summary>
/// Storage service implementation for device token management.
/// Uses Unit of Work pattern for transaction handling.
/// </summary>
public class DeviceTokenStorageService(
    IUnitOfWorkProvider<PushNotificationsDbContext> unitOfWorkProvider,
    ILogger<DeviceTokenStorageService> logger) : IDeviceTokenStorageService
{
    private readonly IUnitOfWorkProvider<PushNotificationsDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly ILogger<DeviceTokenStorageService> _logger = logger;

    public async Task<bool> RegisterTokenAsync(string userId, PushPlatform platform, string token, string? deviceName)
    {
        try
        {
            using var unitOfWork = _unitOfWorkProvider.CreateWritable();
            var repository = unitOfWork.GetRepository<IDeviceTokenRepository>();

            var existingToken = await repository.GetByTokenAsync(token);

            if (existingToken != null)
            {
                // Token exists - update it (upsert behavior)
                existingToken.UserId = userId;
                existingToken.Platform = platform;
                existingToken.DeviceName = deviceName;
                existingToken.LastUsedAt = DateTime.UtcNow;
                existingToken.IsActive = true;

                await repository.UpdateAsync(existingToken);

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

                _logger.LogDebug(
                    "Registered new device token for user {UserId}, platform {Platform}",
                    userId, platform);
            }

            await unitOfWork.CommitAsync();
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

            await repository.DeactivateTokenAsync(token);
            await unitOfWork.CommitAsync();

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
            var count = 0;

            foreach (var token in staleTokens)
            {
                await repository.DeactivateTokenAsync(token.Token);
                count++;
            }

            await unitOfWork.CommitAsync();

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
}
