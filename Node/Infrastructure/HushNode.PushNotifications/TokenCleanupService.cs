using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HushNode.PushNotifications;

/// <summary>
/// Background service that periodically cleans up stale device tokens.
/// Tokens that haven't been used in the configured threshold period are deactivated.
/// </summary>
public class TokenCleanupService(
    IDeviceTokenStorageService deviceTokenStorageService,
    ILogger<TokenCleanupService> logger) : BackgroundService
{
    private readonly IDeviceTokenStorageService _deviceTokenStorageService = deviceTokenStorageService;
    private readonly ILogger<TokenCleanupService> _logger = logger;

    /// <summary>
    /// How often the cleanup job runs (default: once per day).
    /// </summary>
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// Tokens not used in this many days will be deactivated (default: 60 days).
    /// </summary>
    private const int StaleThresholdDays = 60;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Token cleanup service started. Interval: {Interval}, Threshold: {Threshold} days",
            CleanupInterval, StaleThresholdDays);

        // Initial delay to let the application fully start
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token cleanup");
            }

            try
            {
                await Task.Delay(CleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping, just exit the loop
                break;
            }
        }

        _logger.LogInformation("Token cleanup service stopped");
    }

    /// <summary>
    /// Runs a single cleanup cycle, deactivating tokens older than the threshold.
    /// </summary>
    internal async Task RunCleanupAsync()
    {
        var threshold = DateTime.UtcNow.AddDays(-StaleThresholdDays);

        _logger.LogDebug(
            "Starting token cleanup. Deactivating tokens not used since {Threshold}",
            threshold);

        var deactivatedCount = await _deviceTokenStorageService.DeactivateStaleTokensAsync(threshold);

        if (deactivatedCount > 0)
        {
            _logger.LogInformation(
                "Token cleanup complete. Deactivated {Count} stale tokens",
                deactivatedCount);
        }
        else
        {
            _logger.LogDebug("Token cleanup complete. No stale tokens found");
        }
    }
}
