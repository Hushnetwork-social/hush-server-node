using HushNode.PushNotifications.Exceptions;
using HushNode.PushNotifications.Models;
using HushNode.PushNotifications.Providers;
using Microsoft.Extensions.Logging;

namespace HushNode.PushNotifications;

/// <summary>
/// Service for delivering push notifications to users.
/// Coordinates sending to all user devices and handles invalid token cleanup.
/// </summary>
public class PushDeliveryService : IPushDeliveryService
{
    private readonly IDeviceTokenStorageService _tokenStorageService;
    private readonly IFcmProvider _fcmProvider;
    private readonly ILogger<PushDeliveryService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="PushDeliveryService"/>.
    /// </summary>
    /// <param name="tokenStorageService">Service for managing device tokens.</param>
    /// <param name="fcmProvider">FCM provider for sending push notifications.</param>
    /// <param name="logger">Logger instance.</param>
    public PushDeliveryService(
        IDeviceTokenStorageService tokenStorageService,
        IFcmProvider fcmProvider,
        ILogger<PushDeliveryService> logger)
    {
        _tokenStorageService = tokenStorageService;
        _fcmProvider = fcmProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SendPushAsync(string userId, PushPayload payload)
    {
        var deviceTokens = await _tokenStorageService.GetActiveTokensForUserAsync(userId);
        var tokenList = deviceTokens.ToList();

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

    /// <inheritdoc />
    public async Task SendPushToDeviceAsync(DeviceToken deviceToken, PushPayload payload)
    {
        // Currently only Android (FCM) is supported
        if (deviceToken.Platform != PushPlatform.Android)
        {
            _logger.LogDebug(
                "Skipping push notification for unsupported platform {Platform}",
                deviceToken.Platform);
            return;
        }

        try
        {
            await _fcmProvider.SendAsync(deviceToken.Token, payload);
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
    /// Other exceptions are logged but not rethrown to prevent cascade failures.
    /// </summary>
    private async Task SendToDeviceWithErrorHandling(DeviceToken deviceToken, PushPayload payload)
    {
        try
        {
            await SendPushToDeviceAsync(deviceToken, payload);
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
    /// Truncates user ID for logging (privacy - don't log full addresses).
    /// </summary>
    private static string TruncateUserId(string userId)
    {
        if (string.IsNullOrEmpty(userId) || userId.Length <= 16)
            return "[hidden]";

        return $"{userId[..8]}...{userId[^8..]}";
    }
}
