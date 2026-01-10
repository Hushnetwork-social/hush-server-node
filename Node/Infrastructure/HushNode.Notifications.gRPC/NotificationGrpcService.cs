using Grpc.Core;
using Microsoft.Extensions.Logging;
using ProtoTypes = HushNetwork.proto;
using InternalModels = HushNode.Notifications.Models;

namespace HushNode.Notifications.gRPC;

/// <summary>
/// gRPC service implementation for real-time notifications.
/// Exposes notification functionality via gRPC server streaming.
/// </summary>
public class NotificationGrpcService(
    INotificationService notificationService,
    IUnreadTrackingService unreadTrackingService,
    IConnectionTracker connectionTracker,
    ILogger<NotificationGrpcService> logger) : ProtoTypes.HushNotification.HushNotificationBase
{
    private readonly INotificationService _notificationService = notificationService;
    private readonly IUnreadTrackingService _unreadTrackingService = unreadTrackingService;
    private readonly IConnectionTracker _connectionTracker = connectionTracker;
    private readonly ILogger<NotificationGrpcService> _logger = logger;

    /// <summary>
    /// Server streaming RPC - subscribes client to real-time events.
    /// First sends an UNREAD_COUNT_SYNC event with all current counts,
    /// then streams NEW_MESSAGE and MESSAGES_READ events as they occur.
    /// Tracks connection status for notification routing decisions.
    /// </summary>
    public override async Task SubscribeToEvents(
        ProtoTypes.SubscribeToEventsRequest request,
        IServerStreamWriter<ProtoTypes.FeedEvent> responseStream,
        ServerCallContext context)
    {
        var connectionId = Guid.NewGuid().ToString();

        _logger.LogInformation(
            "Client subscribing to events: UserId={UserId}, ConnectionId={ConnectionId}, Platform={Platform}, DeviceId={DeviceId}",
            request.UserId, connectionId, request.Platform, request.DeviceId);

        // Mark user as online when subscription starts
        await _connectionTracker.MarkOnlineAsync(request.UserId, connectionId);

        try
        {
            await foreach (var internalEvent in _notificationService.SubscribeToEventsAsync(
                request.UserId,
                context.CancellationToken))
            {
                // Check if client disconnected before attempting to write
                if (context.CancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation(
                        "Client disconnected before write: UserId={UserId}, ConnectionId={ConnectionId}",
                        request.UserId, connectionId);
                    break;
                }

                var protoEvent = MapToProtoEvent(internalEvent);
                await responseStream.WriteAsync(protoEvent, context.CancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Client disconnected: UserId={UserId}, ConnectionId={ConnectionId}",
                request.UserId, connectionId);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("request is complete"))
        {
            // Client disconnected during write - this is expected during page refresh
            _logger.LogInformation(
                "Client connection closed during write: UserId={UserId}, ConnectionId={ConnectionId}",
                request.UserId, connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error in event stream for UserId={UserId}, ConnectionId={ConnectionId}",
                request.UserId, connectionId);
            throw;
        }
        finally
        {
            // Always mark user as offline when subscription ends
            await _connectionTracker.MarkOfflineAsync(request.UserId, connectionId);
            _logger.LogInformation(
                "Client subscription ended: UserId={UserId}, ConnectionId={ConnectionId}",
                request.UserId, connectionId);
        }
    }

    /// <summary>
    /// Marks all messages in a feed as read for the user.
    /// This resets the unread count to 0 and publishes a MESSAGES_READ event
    /// to all connected devices of the user.
    /// </summary>
    public override async Task<ProtoTypes.MarkFeedAsReadReply> MarkFeedAsRead(
        ProtoTypes.MarkFeedAsReadRequest request,
        ServerCallContext context)
    {
        _logger.LogDebug(
            "MarkFeedAsRead: UserId={UserId}, FeedId={FeedId}",
            request.UserId, request.FeedId);

        try
        {
            // Reset unread count in Redis
            await _unreadTrackingService.MarkFeedAsReadAsync(request.UserId, request.FeedId);

            // Publish event to all connected devices
            await _notificationService.PublishMessagesReadAsync(request.UserId, request.FeedId);

            return new ProtoTypes.MarkFeedAsReadReply { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking feed as read: UserId={UserId}, FeedId={FeedId}",
                request.UserId, request.FeedId);
            return new ProtoTypes.MarkFeedAsReadReply { Success = false };
        }
    }

    /// <summary>
    /// Gets all unread counts for a user's feeds.
    /// </summary>
    public override async Task<ProtoTypes.GetUnreadCountsReply> GetUnreadCounts(
        ProtoTypes.GetUnreadCountsRequest request,
        ServerCallContext context)
    {
        _logger.LogDebug("GetUnreadCounts: UserId={UserId}", request.UserId);

        try
        {
            var counts = await _unreadTrackingService.GetUnreadCountsAsync(request.UserId);

            var reply = new ProtoTypes.GetUnreadCountsReply();
            foreach (var kvp in counts)
            {
                reply.Counts[kvp.Key] = kvp.Value;
            }

            return reply;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unread counts: UserId={UserId}", request.UserId);
            return new ProtoTypes.GetUnreadCountsReply();
        }
    }

    /// <summary>
    /// Maps internal FeedEvent model to proto FeedEvent message.
    /// </summary>
    private static ProtoTypes.FeedEvent MapToProtoEvent(InternalModels.FeedEvent internalEvent)
    {
        var protoEvent = new ProtoTypes.FeedEvent
        {
            Type = MapEventType(internalEvent.Type),
            FeedId = internalEvent.FeedId ?? string.Empty,
            SenderName = internalEvent.SenderName ?? string.Empty,
            MessagePreview = internalEvent.MessagePreview ?? string.Empty,
            UnreadCount = internalEvent.UnreadCount ?? 0,
            TimestampUnixMs = new DateTimeOffset(internalEvent.Timestamp).ToUnixTimeMilliseconds()
        };

        // Add all counts for sync events
        if (internalEvent.AllCounts != null)
        {
            foreach (var kvp in internalEvent.AllCounts)
            {
                protoEvent.AllCounts[kvp.Key] = kvp.Value;
            }
        }

        return protoEvent;
    }

    /// <summary>
    /// Maps internal EventType enum to proto EventType enum.
    /// </summary>
    private static ProtoTypes.EventType MapEventType(InternalModels.EventType internalType)
    {
        return internalType switch
        {
            InternalModels.EventType.NewMessage => ProtoTypes.EventType.NewMessage,
            InternalModels.EventType.MessagesRead => ProtoTypes.EventType.MessagesRead,
            InternalModels.EventType.UnreadCountSync => ProtoTypes.EventType.UnreadCountSync,
            _ => ProtoTypes.EventType.Unspecified
        };
    }
}
