using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using HushNode.PushNotifications.Exceptions;
using HushNode.PushNotifications.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HushNode.PushNotifications.Providers;

/// <summary>
/// Firebase Cloud Messaging (FCM) provider for sending push notifications to Android devices.
/// Implements singleton pattern for FirebaseApp initialization (thread-safe in Firebase SDK).
/// </summary>
public class FcmProvider : IFcmProvider
{
    private readonly ILogger<FcmProvider> _logger;
    private readonly bool _isEnabled;
    private readonly string? _serviceAccountPath;
    private FirebaseMessaging? _messaging;
    private readonly object _initLock = new();
    private bool _isInitialized;

    /// <summary>
    /// Android notification channel ID - must match mobile app configuration.
    /// </summary>
    private const string AndroidChannelId = "hush_messages";

    /// <summary>
    /// Notification icon resource name.
    /// </summary>
    private const string NotificationIcon = "ic_notification";

    /// <summary>
    /// Brand color for Android notifications (Violet-400).
    /// </summary>
    private const string NotificationColor = "#8B5CF6";

    /// <summary>
    /// Click action for navigation when notification is tapped.
    /// </summary>
    private const string ClickAction = "OPEN_FEED";

    /// <summary>
    /// Initializes a new instance of <see cref="FcmProvider"/>.
    /// </summary>
    /// <param name="configuration">Application configuration containing Firebase settings.</param>
    /// <param name="logger">Logger instance.</param>
    public FcmProvider(IConfiguration configuration, ILogger<FcmProvider> logger)
    {
        _logger = logger;

        var firebaseSection = configuration.GetSection("Firebase");
        _isEnabled = firebaseSection.GetValue<bool>("Enabled");
        _serviceAccountPath = firebaseSection.GetValue<string>("ServiceAccountPath");

        if (_isEnabled && string.IsNullOrWhiteSpace(_serviceAccountPath))
        {
            _logger.LogWarning("Firebase is enabled but ServiceAccountPath is not configured. FCM will be disabled.");
            _isEnabled = false;
        }

        _logger.LogInformation("FcmProvider created. Enabled: {IsEnabled}", _isEnabled);
    }

    /// <summary>
    /// Ensures Firebase is initialized. Uses lazy initialization with thread-safe locking.
    /// </summary>
    private void EnsureInitialized()
    {
        if (_isInitialized)
            return;

        lock (_initLock)
        {
            if (_isInitialized)
                return;

            if (!_isEnabled)
            {
                _logger.LogDebug("Firebase is disabled, skipping initialization");
                _isInitialized = true;
                return;
            }

            if (FirebaseApp.DefaultInstance == null)
            {
                _logger.LogInformation("Initializing Firebase from service account: {Path}", _serviceAccountPath);

                FirebaseApp.Create(new AppOptions
                {
                    Credential = GoogleCredential.FromFile(_serviceAccountPath)
                });

                _logger.LogInformation("Firebase initialized successfully");
            }

            _messaging = FirebaseMessaging.DefaultInstance;
            _isInitialized = true;
        }
    }

    /// <inheritdoc />
    public async Task SendAsync(string fcmToken, PushPayload payload)
    {
        if (!_isEnabled)
        {
            _logger.LogDebug("Firebase is disabled, skipping push notification");
            return;
        }

        EnsureInitialized();

        if (_messaging == null)
        {
            _logger.LogWarning("FirebaseMessaging is not available");
            return;
        }

        _logger.LogDebug(
            "Building FCM message. Title: '{Title}', Body: '{Body}', FeedId: {FeedId}",
            payload.Title,
            payload.Body,
            payload.FeedId);

        var message = BuildMessage(fcmToken, payload);

        try
        {
            var response = await _messaging.SendAsync(message);
            _logger.LogDebug("FCM message sent successfully. MessageId: {MessageId}", response);
        }
        catch (FirebaseMessagingException ex) when (ex.MessagingErrorCode == MessagingErrorCode.Unregistered)
        {
            _logger.LogInformation(
                "FCM token is unregistered (device uninstalled app or token expired). Token: {Token}",
                TruncateToken(fcmToken));

            throw new InvalidTokenException("FCM token is no longer valid (Unregistered)", ex);
        }
        catch (FirebaseMessagingException ex) when (ex.MessagingErrorCode == MessagingErrorCode.InvalidArgument)
        {
            _logger.LogWarning(
                "FCM token is invalid. Token: {Token}, Error: {Error}",
                TruncateToken(fcmToken),
                ex.Message);

            throw new InvalidTokenException("FCM token is invalid (InvalidArgument)", ex);
        }
        catch (FirebaseMessagingException ex)
        {
            _logger.LogError(
                ex,
                "FCM error sending message. ErrorCode: {ErrorCode}, Token: {Token}",
                ex.MessagingErrorCode,
                TruncateToken(fcmToken));

            throw;
        }
    }

    /// <summary>
    /// Builds the FCM message with Android-specific configuration.
    /// </summary>
    private static Message BuildMessage(string fcmToken, PushPayload payload)
    {
        var data = payload.Data ?? new Dictionary<string, string>();

        // Add feedId to data for navigation
        if (!string.IsNullOrEmpty(payload.FeedId) && !data.ContainsKey("feedId"))
        {
            data["feedId"] = payload.FeedId;
        }

        // IMPORTANT: Include title and body in data payload as well
        // The Android client reads from data["title"] and data["body"] in FcmService.extractNotificationData
        if (!string.IsNullOrEmpty(payload.Title) && !data.ContainsKey("title"))
        {
            data["title"] = payload.Title;
        }

        if (!string.IsNullOrEmpty(payload.Body) && !data.ContainsKey("body"))
        {
            data["body"] = payload.Body;
        }

        return new Message
        {
            Token = fcmToken,
            Notification = new Notification
            {
                Title = payload.Title,
                Body = payload.Body
            },
            Data = data,
            Android = new AndroidConfig
            {
                Priority = Priority.High,
                Notification = new AndroidNotification
                {
                    Icon = NotificationIcon,
                    Color = NotificationColor,
                    ClickAction = ClickAction,
                    ChannelId = AndroidChannelId
                }
            }
        };
    }

    /// <summary>
    /// Truncates a token for logging (security - don't log full tokens).
    /// </summary>
    private static string TruncateToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length <= 16)
            return "[hidden]";

        return $"{token[..8]}...{token[^8..]}";
    }
}
