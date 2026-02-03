using System.Collections.Concurrent;
using HushNode.Feeds.Storage;
using HushNode.Interfaces.Models;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Idempotency;

/// <summary>
/// Service for checking message idempotency to prevent duplicate transaction processing.
/// FEAT-057: Server Message Idempotency.
///
/// Check order: MemPool first (fast O(1)), then database (slower).
/// This service is registered as Singleton to maintain consistent MemPool tracking across requests.
/// </summary>
public class IdempotencyService(
    IUnitOfWorkProvider<FeedsDbContext> unitOfWorkProvider,
    ILogger<IdempotencyService> logger)
    : IIdempotencyService
{
    private readonly IUnitOfWorkProvider<FeedsDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly ILogger<IdempotencyService> _logger = logger;

    /// <summary>
    /// Thread-safe dictionary tracking message IDs currently in the MemPool.
    /// Key: messageId.ToString(), Value: true (value is unused, using dict for O(1) lookup).
    /// </summary>
    private readonly ConcurrentDictionary<string, bool> _memPoolTracking = new();

    /// <inheritdoc />
    public async Task<IdempotencyCheckResult> CheckAsync(FeedMessageId messageId)
    {
        var messageIdString = messageId.ToString();

        // Step 1: Check MemPool FIRST (faster, in-memory O(1))
        if (_memPoolTracking.ContainsKey(messageIdString))
        {
            _logger.LogDebug(
                "FEAT-057: Message {MessageId} found in MemPool - returning PENDING",
                messageIdString);
            return IdempotencyCheckResult.Pending;
        }

        // Step 2: Check database (slower)
        try
        {
            var existsInDb = await _unitOfWorkProvider
                .CreateReadOnly()
                .GetRepository<IFeedMessageRepository>()
                .ExistsByMessageIdAsync(messageId);

            if (existsInDb)
            {
                _logger.LogDebug(
                    "FEAT-057: Message {MessageId} found in database - returning ALREADY_EXISTS",
                    messageIdString);
                return IdempotencyCheckResult.AlreadyExists;
            }

            _logger.LogDebug(
                "FEAT-057: Message {MessageId} not found - returning ACCEPTED",
                messageIdString);
            return IdempotencyCheckResult.Accepted;
        }
        catch (Exception ex)
        {
            // Fail-closed: On database error, reject the transaction
            _logger.LogError(
                ex,
                "FEAT-057: Database error checking message {MessageId} - returning REJECTED",
                messageIdString);
            return IdempotencyCheckResult.Rejected;
        }
    }

    /// <inheritdoc />
    public bool TryTrackInMemPool(FeedMessageId messageId)
    {
        var messageIdString = messageId.ToString();

        // Atomic TryAdd - returns false if key already exists
        var added = _memPoolTracking.TryAdd(messageIdString, true);

        if (added)
        {
            _logger.LogDebug(
                "FEAT-057: Message {MessageId} added to MemPool tracking",
                messageIdString);
        }
        else
        {
            _logger.LogDebug(
                "FEAT-057: Message {MessageId} already in MemPool tracking - concurrent duplicate",
                messageIdString);
        }

        return added;
    }

    /// <inheritdoc />
    public void RemoveFromTracking(IEnumerable<FeedMessageId> messageIds)
    {
        var removedCount = 0;
        foreach (var messageId in messageIds)
        {
            var messageIdString = messageId.ToString();
            if (_memPoolTracking.TryRemove(messageIdString, out _))
            {
                removedCount++;
            }
        }

        if (removedCount > 0)
        {
            _logger.LogDebug(
                "FEAT-057: Removed {Count} message(s) from MemPool tracking after block finalization",
                removedCount);
        }
    }
}
