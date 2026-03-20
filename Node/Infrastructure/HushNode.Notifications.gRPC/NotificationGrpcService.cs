using Grpc.Core;
using Microsoft.Extensions.Logging;
using HushNode.PushNotifications;
using HushNode.Interfaces.Models;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using ProtoTypes = HushNetwork.proto;
using InternalModels = HushNode.Notifications.Models;

namespace HushNode.Notifications.gRPC;

/// <summary>
/// gRPC service implementation for real-time notifications and device token management.
/// Exposes notification functionality via gRPC server streaming.
/// </summary>
public class NotificationGrpcService(
    INotificationService notificationService,
    ISocialNotificationStateService socialNotificationStateService,
    IUnreadTrackingService unreadTrackingService,
    IConnectionTracker connectionTracker,
    IDeviceTokenStorageService deviceTokenStorageService,
    IFeedReadPositionStorageService readPositionStorageService,
    ILogger<NotificationGrpcService> logger) : ProtoTypes.HushNotification.HushNotificationBase
{
    private const string AuthenticatedUserIdHeader = "x-hush-userid";

    private readonly INotificationService _notificationService = notificationService;
    private readonly ISocialNotificationStateService _socialNotificationStateService = socialNotificationStateService;
    private readonly IUnreadTrackingService _unreadTrackingService = unreadTrackingService;
    private readonly IConnectionTracker _connectionTracker = connectionTracker;
    private readonly IDeviceTokenStorageService _deviceTokenStorageService = deviceTokenStorageService;
    private readonly IFeedReadPositionStorageService _readPositionStorageService = readPositionStorageService;
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
    /// Marks messages in a feed as read for the user up to a specific block index.
    /// This resets the unread count to 0 and publishes a MESSAGES_READ event
    /// to all connected devices of the user.
    /// FEAT-051: Also stores the read position in the database for cross-device sync.
    /// </summary>
    public override async Task<ProtoTypes.MarkFeedAsReadReply> MarkFeedAsRead(
        ProtoTypes.MarkFeedAsReadRequest request,
        ServerCallContext context)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return new ProtoTypes.MarkFeedAsReadReply
            {
                Success = false,
                Message = "UserId is required"
            };
        }

        if (string.IsNullOrWhiteSpace(request.FeedId))
        {
            return new ProtoTypes.MarkFeedAsReadReply
            {
                Success = false,
                Message = "FeedId is required"
            };
        }

        _logger.LogDebug(
            "MarkFeedAsRead: UserId={UserId}, FeedId={FeedId}, UpToBlockIndex={UpToBlockIndex}",
            request.UserId, request.FeedId, request.UpToBlockIndex);

        try
        {
            // FEAT-051: Store read position if block index is provided
            if (request.UpToBlockIndex > 0)
            {
                var feedId = FeedIdHandler.CreateFromString(request.FeedId);
                var blockIndex = new BlockIndex(request.UpToBlockIndex);
                await _readPositionStorageService.MarkFeedAsReadAsync(request.UserId, feedId, blockIndex);
            }

            // Reset unread count in Redis (existing behavior)
            await _unreadTrackingService.MarkFeedAsReadAsync(request.UserId, request.FeedId);

            // FEAT-063: Publish event with upToBlockIndex so receiving devices can calculate remaining unreads
            await _notificationService.PublishMessagesReadAsync(request.UserId, request.FeedId, request.UpToBlockIndex);

            return new ProtoTypes.MarkFeedAsReadReply { Success = true, Message = string.Empty };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking feed as read: UserId={UserId}, FeedId={FeedId}",
                request.UserId, request.FeedId);
            return new ProtoTypes.MarkFeedAsReadReply
            {
                Success = false,
                Message = "Error marking feed as read"
            };
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

    public override async Task<ProtoTypes.GetSocialNotificationInboxResponse> GetSocialNotificationInbox(
        ProtoTypes.GetSocialNotificationInboxRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return new ProtoTypes.GetSocialNotificationInboxResponse();
        }

        EnsureSocialNotificationOwnership(request.UserId, context);

        var result = await _socialNotificationStateService.GetInboxAsync(
            request.UserId,
            request.Limit,
            request.IncludeRead,
            context.CancellationToken);

        var response = new ProtoTypes.GetSocialNotificationInboxResponse
        {
            HasMore = result.HasMore
        };

        response.Items.AddRange(result.Items.Select(MapToProtoSocialNotificationItem));
        return response;
    }

    public override async Task<ProtoTypes.MarkSocialNotificationReadResponse> MarkSocialNotificationRead(
        ProtoTypes.MarkSocialNotificationReadRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return new ProtoTypes.MarkSocialNotificationReadResponse
            {
                Success = false,
                UpdatedCount = 0,
                Message = "UserId is required"
            };
        }

        EnsureSocialNotificationOwnership(request.UserId, context);

        if (!request.MarkAll && string.IsNullOrWhiteSpace(request.NotificationId))
        {
            return new ProtoTypes.MarkSocialNotificationReadResponse
            {
                Success = false,
                UpdatedCount = 0,
                Message = "NotificationId is required when MarkAll is false"
            };
        }

        var updatedCount = await _socialNotificationStateService.MarkAsReadAsync(
            request.UserId,
            request.NotificationId,
            request.MarkAll,
            context.CancellationToken);

        return new ProtoTypes.MarkSocialNotificationReadResponse
        {
            Success = true,
            UpdatedCount = updatedCount,
            Message = string.Empty
        };
    }

    public override async Task<ProtoTypes.GetSocialNotificationPreferencesResponse> GetSocialNotificationPreferences(
        ProtoTypes.GetSocialNotificationPreferencesRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "UserId is required"));
        }

        EnsureSocialNotificationOwnership(request.UserId, context);

        var preferences = await _socialNotificationStateService.GetPreferencesAsync(
            request.UserId,
            context.CancellationToken);

        return new ProtoTypes.GetSocialNotificationPreferencesResponse
        {
            Preferences = MapToProtoSocialNotificationPreferences(preferences)
        };
    }

    public override async Task<ProtoTypes.UpdateSocialNotificationPreferencesResponse> UpdateSocialNotificationPreferences(
        ProtoTypes.UpdateSocialNotificationPreferencesRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return new ProtoTypes.UpdateSocialNotificationPreferencesResponse
            {
                Success = false,
                Message = "UserId is required",
                Preferences = MapToProtoSocialNotificationPreferences(new InternalModels.SocialNotificationPreferences())
            };
        }

        EnsureSocialNotificationOwnership(request.UserId, context);

        var update = new InternalModels.SocialNotificationPreferenceUpdate
        {
            OpenActivityEnabled = request.HasOpenActivityEnabled ? request.OpenActivityEnabled : null,
            CloseActivityEnabled = request.HasCloseActivityEnabled ? request.CloseActivityEnabled : null,
            CircleMutes = request.CircleMutes.Select(x => new InternalModels.SocialCircleMuteState
            {
                CircleId = x.CircleId,
                IsMuted = x.IsMuted
            }).ToList()
        };

        var preferences = await _socialNotificationStateService.UpdatePreferencesAsync(
            request.UserId,
            update,
            context.CancellationToken);

        return new ProtoTypes.UpdateSocialNotificationPreferencesResponse
        {
            Success = true,
            Message = string.Empty,
            Preferences = MapToProtoSocialNotificationPreferences(preferences)
        };
    }

    #region Device Token Management

    /// <summary>
    /// Registers a device token for push notifications.
    /// Supports upsert behavior - updates if token already exists.
    /// </summary>
    public override async Task<ProtoTypes.RegisterDeviceTokenResponse> RegisterDeviceToken(
        ProtoTypes.RegisterDeviceTokenRequest request,
        ServerCallContext context)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return new ProtoTypes.RegisterDeviceTokenResponse
            {
                Success = false,
                Message = "User ID is required"
            };
        }

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return new ProtoTypes.RegisterDeviceTokenResponse
            {
                Success = false,
                Message = "Token is required"
            };
        }

        if (request.Platform == ProtoTypes.PushPlatform.Unspecified)
        {
            return new ProtoTypes.RegisterDeviceTokenResponse
            {
                Success = false,
                Message = "Platform is required"
            };
        }

        try
        {
            var platform = MapProtoPlatformToModel(request.Platform);
            var success = await _deviceTokenStorageService.RegisterTokenAsync(
                request.UserId,
                platform,
                request.Token,
                string.IsNullOrWhiteSpace(request.DeviceName) ? null : request.DeviceName);

            _logger.LogDebug(
                "RegisterDeviceToken: UserId={UserId}, Platform={Platform}, Success={Success}",
                request.UserId, platform, success);

            return new ProtoTypes.RegisterDeviceTokenResponse
            {
                Success = success,
                Message = success ? string.Empty : "Failed to register token"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error registering device token: UserId={UserId}, Platform={Platform}",
                request.UserId, request.Platform);

            return new ProtoTypes.RegisterDeviceTokenResponse
            {
                Success = false,
                Message = "Internal error registering token"
            };
        }
    }

    /// <summary>
    /// Unregisters (deactivates) a device token.
    /// Operation is idempotent - unregistering non-existent token is not an error.
    /// </summary>
    public override async Task<ProtoTypes.UnregisterDeviceTokenResponse> UnregisterDeviceToken(
        ProtoTypes.UnregisterDeviceTokenRequest request,
        ServerCallContext context)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return new ProtoTypes.UnregisterDeviceTokenResponse
            {
                Success = false,
                Message = "User ID is required"
            };
        }

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return new ProtoTypes.UnregisterDeviceTokenResponse
            {
                Success = false,
                Message = "Token is required"
            };
        }

        try
        {
            var success = await _deviceTokenStorageService.UnregisterTokenAsync(
                request.UserId,
                request.Token);

            _logger.LogDebug(
                "UnregisterDeviceToken: UserId={UserId}, Success={Success}",
                request.UserId, success);

            return new ProtoTypes.UnregisterDeviceTokenResponse
            {
                Success = success,
                Message = success ? string.Empty : "Failed to unregister token"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error unregistering device token: UserId={UserId}",
                request.UserId);

            return new ProtoTypes.UnregisterDeviceTokenResponse
            {
                Success = false,
                Message = "Internal error unregistering token"
            };
        }
    }

    /// <summary>
    /// Gets all active device tokens for a user.
    /// Used internally by the push delivery service.
    /// </summary>
    public override async Task<ProtoTypes.GetActiveDeviceTokensResponse> GetActiveDeviceTokens(
        ProtoTypes.GetActiveDeviceTokensRequest request,
        ServerCallContext context)
    {
        var response = new ProtoTypes.GetActiveDeviceTokensResponse();

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return response; // Return empty response for invalid request
        }

        try
        {
            var tokens = await _deviceTokenStorageService.GetActiveTokensForUserAsync(request.UserId);

            foreach (var token in tokens)
            {
                response.Tokens.Add(new ProtoTypes.DeviceTokenInfo
                {
                    Token = token.Token,
                    Platform = MapModelPlatformToProto(token.Platform),
                    DeviceName = token.DeviceName ?? string.Empty
                });
            }

            _logger.LogDebug(
                "GetActiveDeviceTokens: UserId={UserId}, TokenCount={TokenCount}",
                request.UserId, response.Tokens.Count);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error getting active device tokens: UserId={UserId}",
                request.UserId);

            return response; // Return empty response on error
        }
    }

    /// <summary>
    /// Maps proto PushPlatform enum to model PushPlatform enum.
    /// </summary>
    private static PushPlatform MapProtoPlatformToModel(ProtoTypes.PushPlatform protoPlatform)
    {
        return protoPlatform switch
        {
            ProtoTypes.PushPlatform.Android => PushPlatform.Android,
            ProtoTypes.PushPlatform.Ios => PushPlatform.iOS,
            ProtoTypes.PushPlatform.Web => PushPlatform.Web,
            _ => PushPlatform.Unknown
        };
    }

    /// <summary>
    /// Maps model PushPlatform enum to proto PushPlatform enum.
    /// </summary>
    private static ProtoTypes.PushPlatform MapModelPlatformToProto(PushPlatform modelPlatform)
    {
        return modelPlatform switch
        {
            PushPlatform.Android => ProtoTypes.PushPlatform.Android,
            PushPlatform.iOS => ProtoTypes.PushPlatform.Ios,
            PushPlatform.Web => ProtoTypes.PushPlatform.Web,
            _ => ProtoTypes.PushPlatform.Unspecified
        };
    }

    #endregion

    #region Internal Event Mapping

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
            TimestampUnixMs = new DateTimeOffset(internalEvent.Timestamp).ToUnixTimeMilliseconds(),
            UpToBlockIndex = internalEvent.UpToBlockIndex,
            FeedName = internalEvent.FeedName ?? string.Empty
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

    private static ProtoTypes.SocialNotificationItem MapToProtoSocialNotificationItem(
        InternalModels.SocialNotificationItem item)
    {
        var proto = new ProtoTypes.SocialNotificationItem
        {
            NotificationId = item.NotificationId,
            Kind = MapSocialNotificationKind(item.Kind),
            VisibilityClass = MapVisibilityClass(item.VisibilityClass),
            TargetType = MapTargetType(item.TargetType),
            TargetId = item.TargetId,
            PostId = item.PostId,
            ParentCommentId = item.ParentCommentId,
            ActorUserId = item.ActorUserId,
            ActorDisplayName = item.ActorDisplayName,
            Title = item.Title,
            Body = item.Body,
            IsRead = item.IsRead,
            IsPrivatePreviewSuppressed = item.IsPrivatePreviewSuppressed,
            CreatedAtUnixMs = new DateTimeOffset(item.CreatedAtUtc).ToUnixTimeMilliseconds(),
            DeepLinkPath = item.DeepLinkPath
        };

        proto.MatchedCircleIds.AddRange(item.MatchedCircleIds);
        return proto;
    }

    private static ProtoTypes.SocialNotificationPreferences MapToProtoSocialNotificationPreferences(
        InternalModels.SocialNotificationPreferences preferences)
    {
        var proto = new ProtoTypes.SocialNotificationPreferences
        {
            OpenActivityEnabled = preferences.OpenActivityEnabled,
            CloseActivityEnabled = preferences.CloseActivityEnabled,
            UpdatedAtUnixMs = new DateTimeOffset(preferences.UpdatedAtUtc).ToUnixTimeMilliseconds()
        };

        proto.CircleMutes.AddRange(preferences.CircleMutes.Select(x => new ProtoTypes.SocialCircleMuteState
        {
            CircleId = x.CircleId,
            IsMuted = x.IsMuted
        }));

        return proto;
    }

    private static ProtoTypes.SocialNotificationKind MapSocialNotificationKind(
        InternalModels.SocialNotificationKind kind)
    {
        return kind switch
        {
            InternalModels.SocialNotificationKind.NewPost => ProtoTypes.SocialNotificationKind.NewPost,
            InternalModels.SocialNotificationKind.Reaction => ProtoTypes.SocialNotificationKind.Reaction,
            InternalModels.SocialNotificationKind.Comment => ProtoTypes.SocialNotificationKind.Comment,
            InternalModels.SocialNotificationKind.Reply => ProtoTypes.SocialNotificationKind.Reply,
            _ => ProtoTypes.SocialNotificationKind.Unspecified
        };
    }

    private static ProtoTypes.SocialNotificationVisibilityClass MapVisibilityClass(
        InternalModels.SocialNotificationVisibilityClass visibilityClass)
    {
        return visibilityClass switch
        {
            InternalModels.SocialNotificationVisibilityClass.Open => ProtoTypes.SocialNotificationVisibilityClass.Open,
            InternalModels.SocialNotificationVisibilityClass.Close => ProtoTypes.SocialNotificationVisibilityClass.Close,
            _ => ProtoTypes.SocialNotificationVisibilityClass.Unspecified
        };
    }

    private static ProtoTypes.SocialNotificationTargetType MapTargetType(
        InternalModels.SocialNotificationTargetType targetType)
    {
        return targetType switch
        {
            InternalModels.SocialNotificationTargetType.Post => ProtoTypes.SocialNotificationTargetType.Post,
            InternalModels.SocialNotificationTargetType.Comment => ProtoTypes.SocialNotificationTargetType.Comment,
            InternalModels.SocialNotificationTargetType.Reply => ProtoTypes.SocialNotificationTargetType.Reply,
            _ => ProtoTypes.SocialNotificationTargetType.Unspecified
        };
    }

    private static void EnsureSocialNotificationOwnership(string requestUserId, ServerCallContext context)
    {
        var authenticatedUserId = context.RequestHeaders
            .FirstOrDefault(header => string.Equals(header.Key, AuthenticatedUserIdHeader, StringComparison.OrdinalIgnoreCase))
            ?.Value;

        if (string.IsNullOrWhiteSpace(authenticatedUserId))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Authenticated user metadata is required"));
        }

        if (!string.Equals(authenticatedUserId, requestUserId, StringComparison.Ordinal))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Cannot access another user's social notifications"));
        }
    }

    #endregion
}
