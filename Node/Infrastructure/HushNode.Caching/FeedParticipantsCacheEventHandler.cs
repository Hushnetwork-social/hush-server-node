using HushNode.Events;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushNode.Caching;

/// <summary>
/// Event handler that listens for group membership events and updates the feed participants cache.
/// Implements cache invalidation for FEAT-050.
/// </summary>
public class FeedParticipantsCacheEventHandler :
    IHandleAsync<UserJoinedGroupEvent>,
    IHandleAsync<UserLeftGroupEvent>,
    IHandleAsync<UserBannedFromGroupEvent>
{
    private readonly IFeedParticipantsCacheService _cacheService;
    private readonly ILogger<FeedParticipantsCacheEventHandler> _logger;

    /// <summary>
    /// Creates a new instance of FeedParticipantsCacheEventHandler.
    /// </summary>
    /// <param name="cacheService">Feed participants cache service.</param>
    /// <param name="eventAggregator">Event aggregator for subscribing to membership events.</param>
    /// <param name="logger">Logger instance.</param>
    public FeedParticipantsCacheEventHandler(
        IFeedParticipantsCacheService cacheService,
        IEventAggregator eventAggregator,
        ILogger<FeedParticipantsCacheEventHandler> logger)
    {
        _cacheService = cacheService;
        _logger = logger;

        // Subscribe to all membership events for cache invalidation
        eventAggregator.Subscribe(this);

        _logger.LogInformation("FeedParticipantsCacheEventHandler initialized and subscribed to membership events");
    }

    /// <summary>
    /// Handles UserJoinedGroupEvent by adding the user to the participants cache
    /// and invalidating the key generations cache.
    /// </summary>
    public async Task HandleAsync(UserJoinedGroupEvent message)
    {
        try
        {
            _logger.LogDebug(
                "Received UserJoinedGroupEvent for feed {FeedId}, user {Address}",
                message.FeedId.Value,
                TruncateAddress(message.UserPublicAddress));

            // Add participant to cache (if cache exists)
            await _cacheService.AddParticipantAsync(message.FeedId, message.UserPublicAddress);

            // Invalidate key generations cache (keys rotated on join)
            await _cacheService.InvalidateKeyGenerationsAsync(message.FeedId);
        }
        catch (Exception ex)
        {
            // Log and continue - cache updates should never block event processing
            _logger.LogWarning(
                ex,
                "Failed to update cache for UserJoinedGroupEvent. Feed: {FeedId}, User: {Address}",
                message.FeedId.Value,
                TruncateAddress(message.UserPublicAddress));
        }
    }

    /// <summary>
    /// Handles UserLeftGroupEvent by removing the user from the participants cache
    /// and invalidating the key generations cache.
    /// </summary>
    public async Task HandleAsync(UserLeftGroupEvent message)
    {
        try
        {
            _logger.LogDebug(
                "Received UserLeftGroupEvent for feed {FeedId}, user {Address}",
                message.FeedId.Value,
                TruncateAddress(message.UserPublicAddress));

            // Remove participant from cache
            await _cacheService.RemoveParticipantAsync(message.FeedId, message.UserPublicAddress);

            // Invalidate key generations cache (keys rotated on leave)
            await _cacheService.InvalidateKeyGenerationsAsync(message.FeedId);
        }
        catch (Exception ex)
        {
            // Log and continue - cache updates should never block event processing
            _logger.LogWarning(
                ex,
                "Failed to update cache for UserLeftGroupEvent. Feed: {FeedId}, User: {Address}",
                message.FeedId.Value,
                TruncateAddress(message.UserPublicAddress));
        }
    }

    /// <summary>
    /// Handles UserBannedFromGroupEvent by removing the user from the participants cache
    /// and invalidating the key generations cache.
    /// </summary>
    public async Task HandleAsync(UserBannedFromGroupEvent message)
    {
        try
        {
            _logger.LogDebug(
                "Received UserBannedFromGroupEvent for feed {FeedId}, user {Address}",
                message.FeedId.Value,
                TruncateAddress(message.UserPublicAddress));

            // Remove participant from cache
            await _cacheService.RemoveParticipantAsync(message.FeedId, message.UserPublicAddress);

            // Invalidate key generations cache (keys rotated on ban)
            await _cacheService.InvalidateKeyGenerationsAsync(message.FeedId);
        }
        catch (Exception ex)
        {
            // Log and continue - cache updates should never block event processing
            _logger.LogWarning(
                ex,
                "Failed to update cache for UserBannedFromGroupEvent. Feed: {FeedId}, User: {Address}",
                message.FeedId.Value,
                TruncateAddress(message.UserPublicAddress));
        }
    }

    /// <summary>
    /// Truncates a public address for logging purposes.
    /// </summary>
    private static string TruncateAddress(string address)
    {
        return address.Length > 20 ? address.Substring(0, 20) + "..." : address;
    }
}
