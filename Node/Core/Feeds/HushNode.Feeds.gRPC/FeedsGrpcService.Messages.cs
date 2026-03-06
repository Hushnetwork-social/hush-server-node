using Grpc.Core;
using HushNetwork.proto;
using HushNode.Feeds.Storage;
using HushNode.Reactions.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Microsoft.Extensions.Logging;
using MessageReactionTallyModel = HushShared.Reactions.Model.MessageReactionTally;
using Google.Protobuf;
namespace HushNode.Feeds.gRPC;

public partial class FeedsGrpcService
{
    public override async Task<GetFeedMessagesForAddressReply> GetFeedMessagesForAddress(GetFeedMessagesForAddressRequest request, ServerCallContext context)
    {
        try
        {
            // FEAT-052: Read pagination parameters
            var fetchLatest = request.HasFetchLatest && request.FetchLatest;
            var requestedLimit = request.HasLimit ? request.Limit : _maxMessagesPerResponse;
            // Silently cap to server max
            var limit = Math.Min(requestedLimit, _maxMessagesPerResponse);
            // FEAT-052: Backward pagination (scroll-up) - get messages BEFORE a specific block
            BlockIndex? beforeBlockIndex = request.HasBeforeBlockIndex
                ? BlockIndexHandler.CreateNew(request.BeforeBlockIndex)
                : null;

            _logger.LogInformation($"[GetFeedMessagesForAddress] Starting for ProfilePublicKey: {request.ProfilePublicKey?.Substring(0, Math.Min(20, request.ProfilePublicKey?.Length ?? 0))}..., BlockIndex: {request.BlockIndex}, FetchLatest: {fetchLatest}, Limit: {limit}, BeforeBlockIndex: {beforeBlockIndex?.Value.ToString() ?? "null"}, LastReactionTallyVersion: {request.LastReactionTallyVersion}");

            var blockIndex = BlockIndexHandler.CreateNew(request.BlockIndex);

            _logger.LogInformation("[GetFeedMessagesForAddress] Retrieving feeds for address...");
            var lastFeedsFromAddress = await this._feedsStorageService
                .RetrieveFeedsForAddress(request.ProfilePublicKey ?? string.Empty, new BlockIndex(0));

            _logger.LogInformation($"[GetFeedMessagesForAddress] Found {lastFeedsFromAddress.Count()} feeds");

            var reply = new GetFeedMessagesForAddressReply();

            // FEAT-052: Track pagination metadata across all feeds
            var allHasMore = false;
            var allOldestBlockIndex = long.MaxValue;

            // FEAT-065 Pass 1: Collect all messages from all feeds, track unique issuer addresses
            var allFeedMessages = new List<(FeedMessage Message, int FeedIndex)>();
            var uniqueIssuers = new HashSet<string>();

            foreach(var feed in lastFeedsFromAddress)
            {
                _logger.LogInformation($"[GetFeedMessagesForAddress] Processing feed {feed.FeedId.ToString().Substring(0, 8)}..., Type: {feed.FeedType}, BlockIndex filter: {blockIndex.Value}, FetchLatest: {fetchLatest}");

                _logger.LogDebug(
                    "Processing feed {FeedId}, Type: {FeedType}",
                    feed.FeedId,
                    feed.FeedType);

                // FEAT-052: Use paginated query with cache-first pattern
                var paginatedResult = await GetMessagesWithCacheFallbackPaginatedAsync(feed.FeedId, blockIndex, limit, fetchLatest, beforeBlockIndex);
                var messagesList = paginatedResult.Messages.ToList();

                if (paginatedResult.HasMoreMessages)
                    allHasMore = true;

                if (messagesList.Count > 0 && paginatedResult.OldestBlockIndex.Value < allOldestBlockIndex)
                    allOldestBlockIndex = paginatedResult.OldestBlockIndex.Value;

                _logger.LogInformation($"[GetFeedMessagesForAddress] Feed {feed.FeedId.ToString().Substring(0, 8)}... returned {messagesList.Count} messages, HasMore: {paginatedResult.HasMoreMessages}");

                _logger.LogDebug(
                    "Found {MessageCount} messages for feed {FeedId}",
                    messagesList.Count,
                    feed.FeedId);

                foreach (var msg in messagesList)
                {
                    allFeedMessages.Add((msg, 0));
                    if (!string.IsNullOrEmpty(msg.IssuerPublicAddress))
                        uniqueIssuers.Add(msg.IssuerPublicAddress);
                }
            }

            // FEAT-065 Batch resolve: Single HMGET for all unique issuers
            var displayNameMap = await BatchResolveDisplayNamesAsync(uniqueIssuers);

            // FEAT-065 Pass 2: Build response using resolved display names (no per-message DB lookups)
            foreach (var (feedMessage, _) in allFeedMessages)
            {
                var issuerName = displayNameMap.TryGetValue(feedMessage.IssuerPublicAddress, out var name)
                    ? name
                    : await this.ExtractDisplayName(feedMessage.IssuerPublicAddress); // Fallback

                var timestamp = feedMessage.Timestamp?.Value ?? DateTime.UtcNow;

                var feedMessageReply = new GetFeedMessagesForAddressReply.Types.FeedMessage
                {
                    FeedMessageId = feedMessage.FeedMessageId.ToString(),
                    FeedId = feedMessage.FeedId.ToString(),
                    MessageContent = feedMessage.MessageContent,
                    IssuerPublicAddress = feedMessage.IssuerPublicAddress,
                    BlockIndex = feedMessage.BlockIndex.Value,
                    IssuerName = issuerName,
                    TimeStamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)),
                };

                if (feedMessage.AuthorCommitment != null)
                    feedMessageReply.AuthorCommitment = ByteString.CopyFrom(feedMessage.AuthorCommitment);

                if (feedMessage.ReplyToMessageId != null)
                    feedMessageReply.ReplyToMessageId = feedMessage.ReplyToMessageId.ToString();

                if (feedMessage.KeyGeneration != null)
                    feedMessageReply.KeyGeneration = feedMessage.KeyGeneration.Value;

                // FEAT-066: Add attachment metadata from off-chain storage
                await AddAttachmentRefsAsync(feedMessageReply, feedMessage.FeedMessageId);

                reply.Messages.Add(feedMessageReply);
            }

            // FEAT-052: Set pagination metadata
            reply.HasMoreMessages = allHasMore;
            reply.OldestBlockIndex = allOldestBlockIndex == long.MaxValue ? 0 : allOldestBlockIndex;

            // Fetch and add reaction tallies (Protocol Omega)
            await AddReactionTallies(request, reply);

            _logger.LogInformation($"[GetFeedMessagesForAddress] Returning {reply.Messages.Count} total messages, {reply.ReactionTallies.Count} reaction tallies, HasMore: {reply.HasMoreMessages}, OldestBlockIndex: {reply.OldestBlockIndex}");
            return reply;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GetFeedMessagesForAddress] ERROR");
            throw;
        }
    }

    // ===== FEAT-059: Per-feed pagination for scroll-based prefetch =====

    /// <summary>
    /// Get paginated messages for a specific feed.
    /// Used by clients for scroll-based prefetch buffering.
    /// </summary>
    public override async Task<GetFeedMessagesByIdReply> GetFeedMessagesById(
        GetFeedMessagesByIdRequest request,
        ServerCallContext context)
    {
        try
        {
            // Parse request parameters
            var feedId = FeedIdHandler.CreateFromString(request.FeedId);
            var userAddress = request.UserAddress ?? string.Empty;
            var limit = request.HasLimit ? request.Limit : _maxMessagesPerResponse;
            limit = Math.Min(limit, _maxMessagesPerResponse); // Cap to server max

            BlockIndex? beforeBlockIndex = request.HasBeforeBlockIndex
                ? BlockIndexHandler.CreateNew(request.BeforeBlockIndex)
                : null;

            _logger.LogDebug(
                "[GetFeedMessagesById] Request: FeedId={FeedId}, UserAddress={UserAddress}, BeforeBlockIndex={BeforeBlockIndex}, Limit={Limit}",
                request.FeedId,
                userAddress.Substring(0, Math.Min(20, userAddress.Length)),
                beforeBlockIndex?.Value.ToString() ?? "null",
                limit);

            // Check authorization: user must be a participant of the feed
            var isAuthorized = await this._feedsStorageService
                .IsUserParticipantOfFeedAsync(feedId, userAddress);

            if (!isAuthorized)
            {
                _logger.LogWarning(
                    "[GetFeedMessagesById] Authorization failed: User {UserAddress} is not a participant of feed {FeedId}",
                    userAddress.Substring(0, Math.Min(20, userAddress.Length)),
                    request.FeedId);

                return new GetFeedMessagesByIdReply
                {
                    HasMoreMessages = false,
                    OldestBlockIndex = 0,
                    NewestBlockIndex = 0
                };
            }

            // Fetch paginated messages using cache-fallback pattern
            var paginatedResult = await GetMessagesWithCacheFallbackPaginatedAsync(
                feedId,
                new BlockIndex(0), // sinceBlockIndex: fetch from beginning
                limit,
                fetchLatest: beforeBlockIndex == null, // fetch latest if no cursor provided
                beforeBlockIndex);

            var messagesList = paginatedResult.Messages.ToList();

            _logger.LogDebug(
                "[GetFeedMessagesById] Found {MessageCount} messages for feed {FeedId}, HasMore: {HasMore}",
                messagesList.Count,
                request.FeedId,
                paginatedResult.HasMoreMessages);

            // Build response
            var reply = new GetFeedMessagesByIdReply
            {
                HasMoreMessages = paginatedResult.HasMoreMessages,
                OldestBlockIndex = messagesList.Count > 0 ? paginatedResult.OldestBlockIndex.Value : 0,
                NewestBlockIndex = messagesList.Count > 0
                    ? messagesList.Max(m => m.BlockIndex.Value)
                    : 0
            };

            // Map messages to proto format
            foreach (var feedMessage in messagesList)
            {
                var issuerName = await this.ExtractDisplayName(feedMessage.IssuerPublicAddress);
                var timestamp = feedMessage.Timestamp?.Value ?? DateTime.UtcNow;

                var feedMessageProto = new GetFeedMessagesForAddressReply.Types.FeedMessage
                {
                    FeedMessageId = feedMessage.FeedMessageId.ToString(),
                    FeedId = feedMessage.FeedId.ToString(),
                    MessageContent = feedMessage.MessageContent,
                    IssuerPublicAddress = feedMessage.IssuerPublicAddress,
                    BlockIndex = feedMessage.BlockIndex.Value,
                    IssuerName = issuerName,
                    TimeStamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(
                        DateTime.SpecifyKind(timestamp, DateTimeKind.Utc))
                };

                // Add AuthorCommitment if present (Protocol Omega)
                if (feedMessage.AuthorCommitment != null)
                {
                    feedMessageProto.AuthorCommitment = ByteString.CopyFrom(feedMessage.AuthorCommitment);
                }

                // Add ReplyToMessageId if present
                if (feedMessage.ReplyToMessageId != null)
                {
                    feedMessageProto.ReplyToMessageId = feedMessage.ReplyToMessageId.ToString();
                }

                // Add KeyGeneration if present (Group Feeds)
                if (feedMessage.KeyGeneration != null)
                {
                    feedMessageProto.KeyGeneration = feedMessage.KeyGeneration.Value;
                }

                // FEAT-066: Add attachment metadata from off-chain storage
                await AddAttachmentRefsAsync(feedMessageProto, feedMessage.FeedMessageId);

                reply.Messages.Add(feedMessageProto);
            }

            _logger.LogDebug(
                "[GetFeedMessagesById] Returning {MessageCount} messages, OldestBlockIndex={OldestBlockIndex}, NewestBlockIndex={NewestBlockIndex}",
                reply.Messages.Count,
                reply.OldestBlockIndex,
                reply.NewestBlockIndex);

            return reply;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GetFeedMessagesById] Error processing request for feed {FeedId}", request.FeedId);
            throw;
        }
    }

    private async Task AddReactionTallies(GetFeedMessagesForAddressRequest request, GetFeedMessagesForAddressReply reply)
    {
        try
        {
            // Get all feed IDs for this user
            var userFeedIds = await this._feedsStorageService
                .GetFeedIdsForUserAsync(request.ProfilePublicKey);

            if (userFeedIds.Count == 0)
            {
                reply.MaxReactionTallyVersion = request.LastReactionTallyVersion;
                return;
            }

            _logger.LogInformation($"[GetFeedMessagesForAddress] Fetching reaction tallies for {userFeedIds.Count} feeds since version {request.LastReactionTallyVersion}");

            // Get updated tallies using unit of work pattern
            using var unitOfWork = this._reactionsUnitOfWorkProvider.CreateReadOnly();
            var reactionsRepo = unitOfWork.GetRepository<IReactionsRepository>();
            var reactionTallies = await reactionsRepo.GetTalliesForFeedsAsync(userFeedIds, request.LastReactionTallyVersion);

            _logger.LogInformation($"[GetFeedMessagesForAddress] Found {reactionTallies.Count} updated reaction tallies");

            long maxVersion = request.LastReactionTallyVersion;

            foreach (var tally in reactionTallies)
            {
                reply.ReactionTallies.Add(MapTallyToProto(tally));
                maxVersion = Math.Max(maxVersion, tally.Version);
            }

            reply.MaxReactionTallyVersion = maxVersion;
        }
        catch (Exception ex)
        {
            // Don't fail the entire request if reactions fail - just log and continue
            _logger.LogWarning(ex, "[GetFeedMessagesForAddress] Failed to fetch reaction tallies");
            reply.MaxReactionTallyVersion = request.LastReactionTallyVersion;
        }
    }

    private static GetFeedMessagesForAddressReply.Types.MessageReactionTally MapTallyToProto(
        MessageReactionTallyModel tally)
    {
        var proto = new GetFeedMessagesForAddressReply.Types.MessageReactionTally
        {
            MessageId = tally.MessageId.ToString(),
            TallyVersion = tally.Version,
            ReactionCount = tally.TotalCount
        };

        // Map EC points (6 points for each array)
        for (int i = 0; i < 6; i++)
        {
            proto.TallyC1.Add(new ECPoint
            {
                X = ByteString.CopyFrom(tally.TallyC1X[i]),
                Y = ByteString.CopyFrom(tally.TallyC1Y[i])
            });
            proto.TallyC2.Add(new ECPoint
            {
                X = ByteString.CopyFrom(tally.TallyC2X[i]),
                Y = ByteString.CopyFrom(tally.TallyC2Y[i])
            });
        }

        return proto;
    }

    /// <summary>
    /// FEAT-046: Gets messages with cache-first pattern.
    /// Tries Redis cache first, falls back to PostgreSQL on miss or gap.
    /// Implements cache-aside pattern (populates cache on miss).
    /// </summary>
    private async Task<IEnumerable<FeedMessage>> GetMessagesWithCacheFallbackAsync(
        FeedId feedId,
        BlockIndex sinceBlockIndex)
    {
        try
        {
            // 1. Try cache first
            _logger.LogInformation($"[CacheFallback] Feed {feedId.ToString().Substring(0, 8)}...: trying cache with sinceBlockIndex={sinceBlockIndex.Value}");
            var cachedMessages = await this._feedMessageCacheService.GetMessagesAsync(feedId, sinceBlockIndex);

            if (cachedMessages != null)
            {
                var messagesList = cachedMessages.ToList();
                _logger.LogInformation($"[CacheFallback] Feed {feedId.ToString().Substring(0, 8)}...: CACHE HIT - {messagesList.Count} messages");

                // Cache hit - check for gaps if we have messages and a block filter
                if (messagesList.Count > 0 && sinceBlockIndex.Value > 0)
                {
                    var oldestCachedBlock = messagesList.Min(m => m.BlockIndex.Value);

                    if (sinceBlockIndex.Value < oldestCachedBlock)
                    {
                        // Cache gap detected - need to query PostgreSQL for complete data
                        _logger.LogDebug(
                            "Cache gap for feed {FeedId}: requested since block {SinceBlock}, oldest cached is {OldestCached}. Falling back to PostgreSQL.",
                            feedId,
                            sinceBlockIndex.Value,
                            oldestCachedBlock);

                        return await this._feedMessageStorageService
                            .RetrieveLastFeedMessagesForFeedAsync(feedId, sinceBlockIndex);
                    }
                }

                // Cache hit, no gap - return cached messages
                _logger.LogDebug(
                    "Cache HIT for feed {FeedId}: returning {MessageCount} cached messages",
                    feedId,
                    messagesList.Count);

                return messagesList;
            }

            // 2. Cache miss - query PostgreSQL
            _logger.LogInformation($"[CacheFallback] Feed {feedId.ToString().Substring(0, 8)}...: CACHE MISS - querying PostgreSQL");
            _logger.LogDebug(
                "Cache MISS for feed {FeedId}: querying PostgreSQL",
                feedId);

            var dbMessages = await this._feedMessageStorageService
                .RetrieveLastFeedMessagesForFeedAsync(feedId, sinceBlockIndex);

            var dbMessagesList = dbMessages.ToList();
            _logger.LogInformation($"[CacheFallback] Feed {feedId.ToString().Substring(0, 8)}...: PostgreSQL returned {dbMessagesList.Count} messages");

            // 3. Cache-aside: Populate cache with fetched messages (only if we have messages)
            if (dbMessagesList.Count > 0)
            {
                try
                {
                    await this._feedMessageCacheService.PopulateCacheAsync(feedId, dbMessagesList);
                    _logger.LogDebug(
                        "Populated cache for feed {FeedId} with {MessageCount} messages",
                        feedId,
                        dbMessagesList.Count);
                }
                catch (Exception ex)
                {
                    // Log and continue - cache population failure should not fail the request
                    _logger.LogWarning(
                        ex,
                        "Failed to populate cache for feed {FeedId}. Returning PostgreSQL results.",
                        feedId);
                }
            }

            return dbMessagesList;
        }
        catch (Exception ex)
        {
            // Redis error - fall back to PostgreSQL
            _logger.LogWarning(
                ex,
                "Cache operation failed for feed {FeedId}. Falling back to PostgreSQL.",
                feedId);

            return await this._feedMessageStorageService
                .RetrieveLastFeedMessagesForFeedAsync(feedId, sinceBlockIndex);
        }
    }

    /// <summary>
    /// FEAT-052: Gets paginated messages with cache-first pattern.
    /// For fetch_latest=true: Tries Redis cache first (contains latest messages), falls back to PostgreSQL.
    /// For beforeBlockIndex: Backward pagination (scroll-up) - get messages BEFORE a specific block.
    /// For regular pagination: Queries PostgreSQL directly (historical data not in cache).
    /// Returns pagination metadata (hasMore, oldestBlockIndex).
    /// </summary>
    private async Task<PaginatedMessagesResult> GetMessagesWithCacheFallbackPaginatedAsync(
        FeedId feedId,
        BlockIndex sinceBlockIndex,
        int limit,
        bool fetchLatest,
        BlockIndex? beforeBlockIndex = null)
    {
        try
        {
            // FEAT-052: Backward pagination (scroll-up) - always queries PostgreSQL directly
            // Cache contains only the most recent messages, so historical data must come from DB
            if (beforeBlockIndex != null)
            {
                _logger.LogInformation($"[CacheFallbackPaginated] Feed {feedId.ToString().Substring(0, 8)}...: backward pagination (beforeBlockIndex={beforeBlockIndex.Value}), querying PostgreSQL directly");

                var backwardResult = await this._feedMessageStorageService
                    .GetPaginatedMessagesAsync(feedId, sinceBlockIndex, limit, fetchLatest: false, beforeBlockIndex);

                _logger.LogInformation($"[CacheFallbackPaginated] Feed {feedId.ToString().Substring(0, 8)}...: PostgreSQL returned {backwardResult.Messages.Count} messages, HasMore: {backwardResult.HasMoreMessages}");

                return backwardResult;
            }

            if (fetchLatest)
            {
                // For fetch_latest: Try cache first (Redis contains the latest 100 messages)
                _logger.LogInformation($"[CacheFallbackPaginated] Feed {feedId.ToString().Substring(0, 8)}...: fetch_latest=true, trying cache first");
                var cachedMessages = await this._feedMessageCacheService.GetMessagesAsync(feedId, new BlockIndex(0));

                if (cachedMessages != null)
                {
                    var messagesList = cachedMessages.ToList();
                    _logger.LogInformation($"[CacheFallbackPaginated] Feed {feedId.ToString().Substring(0, 8)}...: CACHE HIT - {messagesList.Count} messages");

                    if (messagesList.Count > 0)
                    {
                        // Take the latest N messages (already sorted ascending)
                        var latestMessages = messagesList
                            .OrderByDescending(m => m.BlockIndex.Value)
                            .Take(limit)
                            .OrderBy(m => m.BlockIndex.Value)
                            .ToList();

                        var oldestBlockIndex = latestMessages.Count > 0
                            ? latestMessages[0].BlockIndex
                            : new BlockIndex(0);

                        // Check if there are more messages before the oldest returned
                        var hasMore = messagesList.Count > limit ||
                            await HasOlderMessagesInDbAsync(feedId, oldestBlockIndex);

                        return new PaginatedMessagesResult(latestMessages, hasMore, oldestBlockIndex);
                    }
                }

                // Cache miss or empty - fall through to PostgreSQL
                _logger.LogInformation($"[CacheFallbackPaginated] Feed {feedId.ToString().Substring(0, 8)}...: CACHE MISS - querying PostgreSQL with fetch_latest");
            }
            else
            {
                _logger.LogInformation($"[CacheFallbackPaginated] Feed {feedId.ToString().Substring(0, 8)}...: regular pagination, querying PostgreSQL directly");
            }

            // Query PostgreSQL with pagination
            var result = await this._feedMessageStorageService
                .GetPaginatedMessagesAsync(feedId, sinceBlockIndex, limit, fetchLatest);

            _logger.LogInformation($"[CacheFallbackPaginated] Feed {feedId.ToString().Substring(0, 8)}...: PostgreSQL returned {result.Messages.Count} messages, HasMore: {result.HasMoreMessages}");

            // Cache-aside pattern: Populate cache whenever we fetch from PostgreSQL
            // This ensures subsequent reads can benefit from the cache
            if (result.Messages.Count > 0)
            {
                try
                {
                    await this._feedMessageCacheService.PopulateCacheAsync(feedId, result.Messages.ToList());
                    _logger.LogDebug(
                        "Populated cache for feed {FeedId} with {MessageCount} messages",
                        feedId,
                        result.Messages.Count);
                }
                catch (Exception ex)
                {
                    // Log and continue - cache population failure should not fail the request
                    _logger.LogWarning(
                        ex,
                        "Failed to populate cache for feed {FeedId}. Returning PostgreSQL results.",
                        feedId);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            // Redis error - fall back to PostgreSQL
            _logger.LogWarning(
                ex,
                "Cache operation failed for feed {FeedId}. Falling back to PostgreSQL.",
                feedId);

            return await this._feedMessageStorageService
                .GetPaginatedMessagesAsync(feedId, sinceBlockIndex, limit, fetchLatest);
        }
    }

    /// <summary>
    /// Helper to check if there are older messages in the database before a given block.
    /// Used to determine HasMore when cache doesn't contain all messages.
    /// </summary>
    private async Task<bool> HasOlderMessagesInDbAsync(FeedId feedId, BlockIndex oldestBlockIndex)
    {
        try
        {
            // Query for messages older than the oldest cached message
            var result = await this._feedMessageStorageService
                .GetPaginatedMessagesAsync(feedId, new BlockIndex(0), 1, fetchLatest: false);

            // If there are any messages and the oldest is before our oldest cached, there's more
            return result.Messages.Count > 0 && result.Messages[0].BlockIndex.Value < oldestBlockIndex.Value;
        }
        catch
        {
            // On error, assume there might be more messages
            return true;
        }
    }

    private async Task<Dictionary<string, string>> BatchResolveDisplayNamesAsync(IEnumerable<string> addresses)
    {
        var addressList = addresses.ToList();
        var result = new Dictionary<string, string>();

        if (addressList.Count == 0)
            return result;

        try
        {
            // Pass 1: Batch HMGET from Redis
            var cachedNames = await _identityDisplayNameCacheService.GetDisplayNamesAsync(addressList);

            if (cachedNames != null)
            {
                // Collect hits and misses
                var missingAddresses = new List<string>();
                foreach (var kvp in cachedNames)
                {
                    if (kvp.Value != null)
                        result[kvp.Key] = kvp.Value;
                    else
                        missingAddresses.Add(kvp.Key);
                }

                // Pass 2: Resolve misses from PostgreSQL and populate cache
                if (missingAddresses.Count > 0)
                {
                    var namesToCache = new Dictionary<string, string>();
                    foreach (var address in missingAddresses)
                    {
                        var displayName = await ExtractDisplayName(address);
                        result[address] = displayName;
                        namesToCache[address] = displayName;
                    }

                    // Populate cache for future requests
                    if (namesToCache.Count > 0)
                        _ = _identityDisplayNameCacheService.SetMultipleDisplayNamesAsync(namesToCache);
                }

                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to batch resolve display names from Redis, falling back to PostgreSQL");
        }

        // Redis failure: fall back to per-address PostgreSQL lookup
        foreach (var address in addressList)
        {
            if (!result.ContainsKey(address))
                result[address] = await ExtractDisplayName(address);
        }

        return result;
    }

    private async Task<string> ExtractDisplayName(string publicSigningAddress)
    {
        var identity = await this._identityService.RetrieveIdentityAsync(publicSigningAddress);

        if (identity is Profile profile)
        {
            return profile.Alias;
        }

        // Fallback for NonExistingProfile or unknown types - use first 10 chars of public address
        return publicSigningAddress.Length > 10
            ? publicSigningAddress.Substring(0, 10) + "..."
            : publicSigningAddress;
    }

    /// <summary>
    /// FEAT-066: Add attachment metadata references to a feed message proto response.
    /// Queries PostgreSQL for attachment entities linked to the given message.
    /// Messages without attachments get an empty list (backward compatible).
    /// </summary>
    private async Task AddAttachmentRefsAsync(
        GetFeedMessagesForAddressReply.Types.FeedMessage feedMessageProto,
        FeedMessageId feedMessageId)
    {
        var attachments = await this._attachmentStorageService.GetByMessageIdAsync(feedMessageId);
        foreach (var att in attachments)
        {
            feedMessageProto.Attachments.Add(new GetFeedMessagesForAddressReply.Types.AttachmentRef
            {
                Id = att.Id,
                Hash = att.Hash,
                MimeType = att.MimeType,
                Size = att.OriginalSize,
                FileName = att.FileName
            });
        }
    }

    /// <summary>
    /// Calculate effective BlockIndex for a feed.
    /// Returns MAX of feed BlockIndex and all participants' profile BlockIndex.
    /// This ensures clients detect when any participant updates their identity.
    /// </summary>

    // ===== FEAT-052: GetMessageById Endpoint =====

    public override async Task<GetMessageByIdResponse> GetMessageById(
        GetMessageByIdRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation($"[GetMessageById] FeedId: {request.FeedId}, MessageId: {request.MessageId}");

        try
        {
            // Validate required fields
            if (string.IsNullOrEmpty(request.FeedId))
            {
                return new GetMessageByIdResponse
                {
                    Success = false,
                    Error = "FeedId is required"
                };
            }

            if (string.IsNullOrEmpty(request.MessageId))
            {
                return new GetMessageByIdResponse
                {
                    Success = false,
                    Error = "MessageId is required"
                };
            }

            var feedId = FeedIdHandler.CreateFromString(request.FeedId);
            var messageId = new FeedMessageId(Guid.Parse(request.MessageId));

            // Fetch the message by ID
            var feedMessage = await this._feedMessageStorageService.GetFeedMessageByIdAsync(messageId);

            if (feedMessage == null)
            {
                _logger.LogInformation($"[GetMessageById] Message not found: {request.MessageId}");
                return new GetMessageByIdResponse
                {
                    Success = false,
                    Error = "Message not found"
                };
            }

            // Verify the message belongs to the requested feed
            if (feedMessage.FeedId != feedId)
            {
                _logger.LogInformation($"[GetMessageById] Message {request.MessageId} does not belong to feed {request.FeedId}");
                return new GetMessageByIdResponse
                {
                    Success = false,
                    Error = "Message not found"
                };
            }

            // Resolve issuer display name
            var issuerName = await this.ExtractDisplayName(feedMessage.IssuerPublicAddress);

            // Handle potential null or invalid timestamp
            var timestamp = feedMessage.Timestamp?.Value ?? DateTime.UtcNow;

            var messageProto = new GetFeedMessagesForAddressReply.Types.FeedMessage
            {
                FeedMessageId = feedMessage.FeedMessageId.ToString(),
                FeedId = feedMessage.FeedId.ToString(),
                MessageContent = feedMessage.MessageContent,
                IssuerPublicAddress = feedMessage.IssuerPublicAddress,
                BlockIndex = feedMessage.BlockIndex.Value,
                IssuerName = issuerName,
                TimeStamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(
                    DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)),
            };

            // Add AuthorCommitment if present (Protocol Omega)
            if (feedMessage.AuthorCommitment != null)
            {
                messageProto.AuthorCommitment = ByteString.CopyFrom(feedMessage.AuthorCommitment);
            }

            // Add ReplyToMessageId if present
            if (feedMessage.ReplyToMessageId != null)
            {
                messageProto.ReplyToMessageId = feedMessage.ReplyToMessageId.ToString();
            }

            // Add KeyGeneration if present (Group Feeds)
            if (feedMessage.KeyGeneration != null)
            {
                messageProto.KeyGeneration = feedMessage.KeyGeneration.Value;
            }

            // FEAT-066: Include attachment metadata
            await AddAttachmentRefsAsync(messageProto, feedMessage.FeedMessageId);

            _logger.LogInformation($"[GetMessageById] Success - returning message from feed {feedMessage.FeedId}");
            return new GetMessageByIdResponse
            {
                Success = true,
                Message = messageProto
            };
        }
        catch (FormatException ex)
        {
            _logger.LogInformation($"[GetMessageById] Invalid ID format: {ex.Message}");
            return new GetMessageByIdResponse
            {
                Success = false,
                Error = "Invalid message ID format"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GetMessageById] ERROR");
            return new GetMessageByIdResponse
            {
                Success = false,
                Error = $"Internal error: {ex.Message}"
            };
        }
    }

    // ===== FEAT-066: Attachment Download =====

    public override async Task DownloadAttachment(
        DownloadAttachmentRequest request,
        IServerStreamWriter<AttachmentChunk> responseStream,
        ServerCallContext context)
    {
        // Authorization: requester must be a participant of the feed
        var feedId = new FeedId(Guid.Parse(request.FeedId));
        var isAuthorized = await this._feedsStorageService
            .IsUserParticipantOfFeedAsync(feedId, request.RequesterUserAddress);

        if (!isAuthorized)
        {
            _logger.LogWarning(
                "[DownloadAttachment] Authorization failed: user {UserAddress} is not a participant of feed {FeedId}",
                request.RequesterUserAddress.Substring(0, Math.Min(20, request.RequesterUserAddress.Length)),
                request.FeedId);
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Not a participant of this feed"));
        }

        // Fetch attachment from storage
        var attachment = await this._attachmentStorageService.GetByIdAsync(request.AttachmentId);
        if (attachment == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Attachment '{request.AttachmentId}' not found"));
        }

        // Select original or thumbnail bytes
        byte[]? bytesToStream;
        if (request.ThumbnailOnly)
        {
            bytesToStream = attachment.EncryptedThumbnail;
            if (bytesToStream == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"No thumbnail available for attachment '{request.AttachmentId}'"));
            }
        }
        else
        {
            bytesToStream = attachment.EncryptedOriginal;
        }

        // Chunk and stream
        var totalSize = bytesToStream.Length;
        var totalChunks = (int)Math.Ceiling((double)totalSize / AttachmentChunkSize);

        for (var i = 0; i < totalChunks; i++)
        {
            var offset = i * AttachmentChunkSize;
            var length = Math.Min(AttachmentChunkSize, totalSize - offset);

            var chunk = new AttachmentChunk
            {
                Data = Google.Protobuf.ByteString.CopyFrom(bytesToStream, offset, length),
                ChunkIndex = i,
                TotalChunks = i == 0 ? totalChunks : 0,
                TotalSize = i == 0 ? totalSize : 0
            };

            await responseStream.WriteAsync(chunk);
        }

        _logger.LogDebug(
            "[DownloadAttachment] Streamed {TotalChunks} chunks ({TotalSize} bytes) for attachment {AttachmentId}",
            totalChunks, totalSize, request.AttachmentId);
    }

    public override async Task DownloadSocialPostAttachment(
        DownloadSocialPostAttachmentRequest request,
        IServerStreamWriter<AttachmentChunk> responseStream,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.PostId, out var postId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid PostId"));
        }

        var post = await this._feedsStorageService.GetSocialPostAsync(postId);
        if (post == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Social post '{request.PostId}' not found"));
        }

        var requester = request.RequesterPublicAddress?.Trim();
        var isRequesterAuthor = request.IsAuthenticated &&
                                !string.IsNullOrWhiteSpace(requester) &&
                                string.Equals(requester, post.AuthorPublicAddress, StringComparison.Ordinal);

        var canView = post.AudienceVisibility == SocialPostVisibility.Open;
        if (!canView && request.IsAuthenticated && !string.IsNullOrWhiteSpace(requester))
        {
            var circleFeedIds = post.AudienceCircles.Select(x => x.CircleFeedId).ToArray();
            var hasCircleAccess = await this._feedsStorageService.IsUserInAnyActiveCircleAsync(requester, circleFeedIds);
            canView = isRequesterAuthor || hasCircleAccess;
        }

        if (!canView)
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Not authorized to access this social post attachment"));
        }

        var attachment = await this._attachmentStorageService.GetByIdAsync(request.AttachmentId);
        if (attachment == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Attachment '{request.AttachmentId}' not found"));
        }

        if (!string.Equals(attachment.FeedMessageId.ToString(), request.PostId, StringComparison.OrdinalIgnoreCase))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Attachment does not belong to target social post"));
        }

        var bytesToStream = attachment.EncryptedOriginal;
        var totalSize = bytesToStream.Length;
        var totalChunks = (int)Math.Ceiling((double)totalSize / AttachmentChunkSize);

        for (var i = 0; i < totalChunks; i++)
        {
            var offset = i * AttachmentChunkSize;
            var length = Math.Min(AttachmentChunkSize, totalSize - offset);

            var chunk = new AttachmentChunk
            {
                Data = Google.Protobuf.ByteString.CopyFrom(bytesToStream, offset, length),
                ChunkIndex = i,
                TotalChunks = i == 0 ? totalChunks : 0,
                TotalSize = i == 0 ? totalSize : 0
            };

            await responseStream.WriteAsync(chunk);
        }
    }
}


