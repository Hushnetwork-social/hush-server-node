using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using HushNode.Notifications.Models;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HushNode.Notifications;

/// <summary>
/// Redis-backed implementation of the notification service.
/// Uses Redis Pub/Sub for real-time event distribution.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly RedisConnectionManager _redis;
    private readonly IUnreadTrackingService _unreadTracking;
    private readonly ILogger<NotificationService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public NotificationService(
        RedisConnectionManager redis,
        IUnreadTrackingService unreadTracking,
        ILogger<NotificationService> logger)
    {
        _redis = redis;
        _unreadTracking = unreadTracking;
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<FeedEvent> SubscribeToEventsAsync(
        string userId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = _redis.GetUserChannel(userId);
        var queue = Channel.CreateUnbounded<FeedEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        // Subscribe to Redis channel
        await _redis.Subscriber.SubscribeAsync(channel, (_, message) =>
        {
            if (message.HasValue)
            {
                try
                {
                    var feedEvent = JsonSerializer.Deserialize<FeedEvent>(message!, JsonOptions);
                    if (feedEvent != null)
                    {
                        queue.Writer.TryWrite(feedEvent);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize feed event");
                }
            }
        });

        _logger.LogInformation("User {UserId} subscribed to notification events", userId);

        try
        {
            // First, send a sync event with all current unread counts
            var unreadCounts = await _unreadTracking.GetUnreadCountsAsync(userId);
            var syncEvent = new FeedEvent
            {
                Type = EventType.UnreadCountSync,
                AllCounts = unreadCounts,
                Timestamp = DateTime.UtcNow
            };
            yield return syncEvent;

            // Then yield real-time events as they arrive
            await foreach (var evt in queue.Reader.ReadAllAsync(cancellationToken))
            {
                yield return evt;
            }
        }
        finally
        {
            // Cleanup on disconnect
            await _redis.Subscriber.UnsubscribeAsync(channel);
            queue.Writer.Complete();
            _logger.LogInformation("User {UserId} unsubscribed from notification events", userId);
        }
    }

    /// <inheritdoc />
    public async Task PublishNewMessageAsync(
        string recipientUserId,
        string feedId,
        string senderName,
        string messagePreview)
    {
        try
        {
            // Get current unread count (after increment by caller)
            var unreadCount = await _unreadTracking.GetUnreadCountAsync(recipientUserId, feedId);

            var evt = new FeedEvent
            {
                Type = EventType.NewMessage,
                FeedId = feedId,
                SenderName = senderName,
                MessagePreview = TruncateMessage(messagePreview, 255),
                UnreadCount = unreadCount,
                Timestamp = DateTime.UtcNow
            };

            var channel = _redis.GetUserChannel(recipientUserId);
            var json = JsonSerializer.Serialize(evt, JsonOptions);
            await _redis.Subscriber.PublishAsync(channel, json);

            _logger.LogDebug(
                "Published NewMessage event for user {UserId}, feed {FeedId}",
                recipientUserId, feedId);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis connection error while publishing new message event");
        }
    }

    /// <inheritdoc />
    public async Task PublishMessagesReadAsync(string userId, string feedId)
    {
        try
        {
            var evt = new FeedEvent
            {
                Type = EventType.MessagesRead,
                FeedId = feedId,
                UnreadCount = 0,
                Timestamp = DateTime.UtcNow
            };

            var channel = _redis.GetUserChannel(userId);
            var json = JsonSerializer.Serialize(evt, JsonOptions);
            await _redis.Subscriber.PublishAsync(channel, json);

            _logger.LogDebug(
                "Published MessagesRead event for user {UserId}, feed {FeedId}",
                userId, feedId);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis connection error while publishing messages read event");
        }
    }

    private static string TruncateMessage(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        if (content.Length <= maxLength) return content;
        return content[..(maxLength - 3)] + "...";
    }
}
