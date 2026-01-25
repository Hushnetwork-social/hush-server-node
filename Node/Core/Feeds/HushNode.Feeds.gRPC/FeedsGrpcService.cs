using Grpc.Core;
using HushNetwork.proto;
using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushNode.Identity;
using HushNode.Identity.Storage;
using HushNode.Reactions.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using MessageReactionTallyModel = HushShared.Reactions.Model.MessageReactionTally;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Olimpo;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Feeds.gRPC;

public class FeedsGrpcService(
    IFeedsStorageService feedsStorageService,
    IFeedMessageStorageService feedMessageStorageService,
    IFeedMessageCacheService feedMessageCacheService,
    IFeedParticipantsCacheService feedParticipantsCacheService,
    IGroupMembersCacheService groupMembersCacheService,
    IFeedReadPositionStorageService feedReadPositionStorageService,
    IIdentityService identityService,
    IIdentityStorageService identityStorageService,
    IBlockchainCache blockchainCache,
    IUnitOfWorkProvider<ReactionsDbContext> reactionsUnitOfWorkProvider,
    IConfiguration configuration,
    ILogger<FeedsGrpcService> logger) : HushFeed.HushFeedBase
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IFeedMessageStorageService _feedMessageStorageService = feedMessageStorageService;
    private readonly IFeedMessageCacheService _feedMessageCacheService = feedMessageCacheService;
    private readonly IFeedParticipantsCacheService _feedParticipantsCacheService = feedParticipantsCacheService;
    private readonly IGroupMembersCacheService _groupMembersCacheService = groupMembersCacheService;
    private readonly IFeedReadPositionStorageService _feedReadPositionStorageService = feedReadPositionStorageService;
    private readonly IIdentityService _identityService = identityService;
    private readonly IIdentityStorageService _identityStorageService = identityStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IUnitOfWorkProvider<ReactionsDbContext> _reactionsUnitOfWorkProvider = reactionsUnitOfWorkProvider;
    private readonly int _maxMessagesPerResponse = configuration.GetValue<int>("Feeds:MaxMessagesPerResponse", 100);
    private readonly ILogger<FeedsGrpcService> _logger = logger;

    /// <summary>Maximum number of members supported in a single key rotation.</summary>
    private const int MaxMembersPerRotation = 512;

    public override async Task<HasPersonalFeedReply> HasPersonalFeed(
        HasPersonalFeedRequest request, 
        ServerCallContext context)
    {
        var hasPersonalFeed = await this._feedsStorageService.HasPersonalFeed(request.PublicPublicKey);

        return new HasPersonalFeedReply
        {
            FeedAvailable = hasPersonalFeed
        };
    }

    public override async Task<IsFeedInBlockchainReply> IsFeedInBlockchain(IsFeedInBlockchainRequest request, ServerCallContext context)
    {
        var isFeedInBlockchain = await this._feedsStorageService
            .IsFeedIsBlockchain(FeedIdHandler.CreateFromString(request.FeedId));

        return new IsFeedInBlockchainReply
        {
            FeedAvailable = isFeedInBlockchain
        };
    }

    public override async Task<GetFeedForAddressReply> GetFeedsForAddress(GetFeedForAddressRequest request, ServerCallContext context)
    {
        var blockIndex = BlockIndexHandler
            .CreateNew(request.BlockIndex);

        _logger.LogInformation(
            "[GetFeedsForAddress] Request: ProfilePublicKey={ProfilePublicKey}, BlockIndex={BlockIndex}",
            request.ProfilePublicKey?.Substring(0, Math.Min(20, request.ProfilePublicKey?.Length ?? 0)),
            request.BlockIndex);

        // Return empty result if no profile key provided
        if (string.IsNullOrEmpty(request.ProfilePublicKey))
        {
            _logger.LogWarning("[GetFeedsForAddress] No ProfilePublicKey provided, returning empty result");
            return new GetFeedForAddressReply();
        }

        var lastFeeds = await this._feedsStorageService
            .RetrieveFeedsForAddress(request.ProfilePublicKey, blockIndex);

        _logger.LogInformation(
            "[GetFeedsForAddress] Found {FeedCount} feed(s) for address",
            lastFeeds.Count());

        // FEAT-051: Batch fetch read positions for all feeds (cache-aside pattern with DB fallback)
        IReadOnlyDictionary<FeedId, BlockIndex> readPositions;
        try
        {
            readPositions = await _feedReadPositionStorageService.GetReadPositionsForUserAsync(request.ProfilePublicKey);
        }
        catch (Exception ex)
        {
            // Graceful degradation: if read positions fail, continue with empty dictionary (all feeds show 0)
            _logger.LogWarning(ex, "Failed to fetch read positions for user {UserId}, defaulting to 0", request.ProfilePublicKey);
            readPositions = new Dictionary<FeedId, BlockIndex>();
        }

        var reply = new GetFeedForAddressReply();
        foreach(var feed in lastFeeds)
        {
            // TODO [AboimPinto] Here tghe FeedTitle should be calculated
            // * PersonalFeed -> ProfileAlias + (YOU) / First 10 characters of the public address + (YOU)
            // * ChatFeed -> Other chat participant ProfileAlias

            var feedAlias = feed.FeedType switch
            {
                FeedType.Personal => await ExtractPersonalFeedAlias(feed),
                FeedType.Chat => await ExtractChatFeedAlias(feed, request.ProfilePublicKey),
                FeedType.Group => await ExtractGroupFeedAlias(feed),
                FeedType.Broadcast => await ExtractBroascastAlias(feed),
                _ => throw new InvalidOperationException($"the FeedTYype {feed.FeedType} is not supported.")
            };

            // Calculate effective BlockIndex: MAX of feed BlockIndex and all participants' profile BlockIndex
            // This ensures clients detect changes when any participant updates their identity
            var effectiveBlockIndex = await GetEffectiveBlockIndex(feed);

            // FEAT-051: Get read position for this feed (default to 0 if not found)
            var lastReadBlockIndex = readPositions.TryGetValue(feed.FeedId, out var readPosition)
                ? readPosition.Value
                : 0;

            var replyFeed = new GetFeedForAddressReply.Types.Feed
            {
                FeedId = feed.FeedId.ToString(),
                FeedTitle = feedAlias,
                FeedType = (int)feed.FeedType,
                BlockIndex = effectiveBlockIndex,
                LastReadBlockIndex = lastReadBlockIndex
            };

            foreach(var participant in feed.Participants)
            {
                replyFeed.FeedParticipants.Add(new GetFeedForAddressReply.Types.FeedParticipant
                {
                    FeedId = participant.FeedId.ToString(),
                    ParticipantPublicAddress = participant.ParticipantPublicAddress,
                    ParticipantType = (int)participant.ParticipantType,
                    EncryptedFeedKey = participant.EncryptedFeedKey
                });
            }

            reply.Feeds.Add(replyFeed);
        }

        return reply;
    }

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

            Console.WriteLine($"[GetFeedMessagesForAddress] Starting for ProfilePublicKey: {request.ProfilePublicKey?.Substring(0, Math.Min(20, request.ProfilePublicKey?.Length ?? 0))}..., BlockIndex: {request.BlockIndex}, FetchLatest: {fetchLatest}, Limit: {limit}, BeforeBlockIndex: {beforeBlockIndex?.Value.ToString() ?? "null"}, LastReactionTallyVersion: {request.LastReactionTallyVersion}");

            var blockIndex = BlockIndexHandler.CreateNew(request.BlockIndex);

            Console.WriteLine("[GetFeedMessagesForAddress] Retrieving feeds for address...");
            var lastFeedsFromAddress = await this._feedsStorageService
                .RetrieveFeedsForAddress(request.ProfilePublicKey ?? string.Empty, new BlockIndex(0));

            Console.WriteLine($"[GetFeedMessagesForAddress] Found {lastFeedsFromAddress.Count()} feeds");

            var reply = new GetFeedMessagesForAddressReply();

            // FEAT-052: Track pagination metadata across all feeds
            var allHasMore = false;
            var allOldestBlockIndex = long.MaxValue;

            foreach(var feed in lastFeedsFromAddress)
            {
                Console.WriteLine($"[GetFeedMessagesForAddress] Processing feed {feed.FeedId.ToString().Substring(0, 8)}..., Type: {feed.FeedType}, BlockIndex filter: {blockIndex.Value}, FetchLatest: {fetchLatest}");

                _logger.LogDebug(
                    "Processing feed {FeedId}, Type: {FeedType}",
                    feed.FeedId,
                    feed.FeedType);

                // FEAT-052: Use paginated query with cache-first pattern
                var paginatedResult = await GetMessagesWithCacheFallbackPaginatedAsync(feed.FeedId, blockIndex, limit, fetchLatest, beforeBlockIndex);
                var messagesList = paginatedResult.Messages.ToList();

                // Track if any feed has more messages
                if (paginatedResult.HasMoreMessages)
                {
                    allHasMore = true;
                }

                // Track the oldest block index across all feeds
                if (messagesList.Count > 0 && paginatedResult.OldestBlockIndex.Value < allOldestBlockIndex)
                {
                    allOldestBlockIndex = paginatedResult.OldestBlockIndex.Value;
                }

                Console.WriteLine($"[GetFeedMessagesForAddress] Feed {feed.FeedId.ToString().Substring(0, 8)}... returned {messagesList.Count} messages, HasMore: {paginatedResult.HasMoreMessages}");
                foreach (var msg in messagesList)
                {
                    var keyGen = msg.KeyGeneration ?? -1;
                    Console.WriteLine($"[GetFeedMessagesForAddress]   - Message ID: {msg.FeedMessageId.ToString().Substring(0, 8)}..., BlockIndex: {msg.BlockIndex.Value}, KeyGen: {keyGen}");
                }

                _logger.LogDebug(
                    "Found {MessageCount} messages for feed {FeedId}",
                    messagesList.Count,
                    feed.FeedId);

                foreach (var feedMessage in messagesList)
                {
                    Console.WriteLine($"[GetFeedMessagesForAddress] Processing message: {feedMessage.FeedMessageId}, Timestamp: {feedMessage.Timestamp?.Value}");

                    var issuerName = await this.ExtractDisplayName(feedMessage.IssuerPublicAddress);
                    Console.WriteLine($"[GetFeedMessagesForAddress] IssuerName resolved: {issuerName}");

                    // Handle potential null or invalid timestamp
                    var timestamp = feedMessage.Timestamp?.Value ?? DateTime.UtcNow;
                    Console.WriteLine($"[GetFeedMessagesForAddress] Timestamp to convert: {timestamp}, Kind: {timestamp.Kind}");

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

                    // Add AuthorCommitment if present (Protocol Omega)
                    if (feedMessage.AuthorCommitment != null)
                    {
                        feedMessageReply.AuthorCommitment = ByteString.CopyFrom(feedMessage.AuthorCommitment);
                    }

                    // Add ReplyToMessageId if present (Reply to Message feature)
                    if (feedMessage.ReplyToMessageId != null)
                    {
                        feedMessageReply.ReplyToMessageId = feedMessage.ReplyToMessageId.ToString();
                    }

                    // Add KeyGeneration if present (Group Feeds)
                    if (feedMessage.KeyGeneration != null)
                    {
                        feedMessageReply.KeyGeneration = feedMessage.KeyGeneration.Value;
                    }

                    reply.Messages.Add(feedMessageReply);

                    Console.WriteLine($"[GetFeedMessagesForAddress] Message added successfully");
                }
            }

            // FEAT-052: Set pagination metadata
            reply.HasMoreMessages = allHasMore;
            reply.OldestBlockIndex = allOldestBlockIndex == long.MaxValue ? 0 : allOldestBlockIndex;

            // Fetch and add reaction tallies (Protocol Omega)
            await AddReactionTallies(request, reply);

            Console.WriteLine($"[GetFeedMessagesForAddress] Returning {reply.Messages.Count} total messages, {reply.ReactionTallies.Count} reaction tallies, HasMore: {reply.HasMoreMessages}, OldestBlockIndex: {reply.OldestBlockIndex}");
            return reply;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GetFeedMessagesForAddress] ERROR: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[GetFeedMessagesForAddress] Stack trace: {ex.StackTrace}");
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

            Console.WriteLine($"[GetFeedMessagesForAddress] Fetching reaction tallies for {userFeedIds.Count} feeds since version {request.LastReactionTallyVersion}");

            // Get updated tallies using unit of work pattern
            using var unitOfWork = this._reactionsUnitOfWorkProvider.CreateReadOnly();
            var reactionsRepo = unitOfWork.GetRepository<IReactionsRepository>();
            var reactionTallies = await reactionsRepo.GetTalliesForFeedsAsync(userFeedIds, request.LastReactionTallyVersion);

            Console.WriteLine($"[GetFeedMessagesForAddress] Found {reactionTallies.Count} updated reaction tallies");

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
            Console.WriteLine($"[GetFeedMessagesForAddress] Warning: Failed to fetch reaction tallies: {ex.Message}");
            Console.WriteLine($"[GetFeedMessagesForAddress] Exception type: {ex.GetType().Name}");
            Console.WriteLine($"[GetFeedMessagesForAddress] Stack trace: {ex.StackTrace}");
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
            Console.WriteLine($"[CacheFallback] Feed {feedId.ToString().Substring(0, 8)}...: trying cache with sinceBlockIndex={sinceBlockIndex.Value}");
            var cachedMessages = await this._feedMessageCacheService.GetMessagesAsync(feedId, sinceBlockIndex);

            if (cachedMessages != null)
            {
                var messagesList = cachedMessages.ToList();
                Console.WriteLine($"[CacheFallback] Feed {feedId.ToString().Substring(0, 8)}...: CACHE HIT - {messagesList.Count} messages");

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
            Console.WriteLine($"[CacheFallback] Feed {feedId.ToString().Substring(0, 8)}...: CACHE MISS - querying PostgreSQL");
            _logger.LogDebug(
                "Cache MISS for feed {FeedId}: querying PostgreSQL",
                feedId);

            var dbMessages = await this._feedMessageStorageService
                .RetrieveLastFeedMessagesForFeedAsync(feedId, sinceBlockIndex);

            var dbMessagesList = dbMessages.ToList();
            Console.WriteLine($"[CacheFallback] Feed {feedId.ToString().Substring(0, 8)}...: PostgreSQL returned {dbMessagesList.Count} messages");

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
                Console.WriteLine($"[CacheFallbackPaginated] Feed {feedId.ToString().Substring(0, 8)}...: backward pagination (beforeBlockIndex={beforeBlockIndex.Value}), querying PostgreSQL directly");

                var backwardResult = await this._feedMessageStorageService
                    .GetPaginatedMessagesAsync(feedId, sinceBlockIndex, limit, fetchLatest: false, beforeBlockIndex);

                Console.WriteLine($"[CacheFallbackPaginated] Feed {feedId.ToString().Substring(0, 8)}...: PostgreSQL returned {backwardResult.Messages.Count} messages, HasMore: {backwardResult.HasMoreMessages}");

                return backwardResult;
            }

            if (fetchLatest)
            {
                // For fetch_latest: Try cache first (Redis contains the latest 100 messages)
                Console.WriteLine($"[CacheFallbackPaginated] Feed {feedId.ToString().Substring(0, 8)}...: fetch_latest=true, trying cache first");
                var cachedMessages = await this._feedMessageCacheService.GetMessagesAsync(feedId, new BlockIndex(0));

                if (cachedMessages != null)
                {
                    var messagesList = cachedMessages.ToList();
                    Console.WriteLine($"[CacheFallbackPaginated] Feed {feedId.ToString().Substring(0, 8)}...: CACHE HIT - {messagesList.Count} messages");

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
                Console.WriteLine($"[CacheFallbackPaginated] Feed {feedId.ToString().Substring(0, 8)}...: CACHE MISS - querying PostgreSQL with fetch_latest");
            }
            else
            {
                Console.WriteLine($"[CacheFallbackPaginated] Feed {feedId.ToString().Substring(0, 8)}...: regular pagination, querying PostgreSQL directly");
            }

            // Query PostgreSQL with pagination
            var result = await this._feedMessageStorageService
                .GetPaginatedMessagesAsync(feedId, sinceBlockIndex, limit, fetchLatest);

            Console.WriteLine($"[CacheFallbackPaginated] Feed {feedId.ToString().Substring(0, 8)}...: PostgreSQL returned {result.Messages.Count} messages, HasMore: {result.HasMoreMessages}");

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

    private Task<string> ExtractBroascastAlias(Feed feed)
    {
        throw new NotImplementedException();
    }

    private async Task<string> ExtractChatFeedAlias(Feed feed, string requesterPublicAddress)
    {
        var otherParticipantPublicAddress = feed.Participants
            .Single(x => x.ParticipantPublicAddress != requesterPublicAddress)
            .ParticipantPublicAddress;

        return await this.ExtractDisplayName(otherParticipantPublicAddress);
    }

    private async Task<string> ExtractPersonalFeedAlias(Feed feed)
    {
        var displayName = await this.ExtractDisplayName(feed.Participants.Single().ParticipantPublicAddress);

        return string.Format("{0} (YOU)", displayName);
    }

    private async Task<string> ExtractGroupFeedAlias(Feed feed)
    {
        var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feed.FeedId);

        if (groupFeed != null)
        {
            return groupFeed.Title;
        }

        // Fallback: use the alias from the Feed record if GroupFeed lookup fails
        return feed.Alias;
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
    /// Calculate effective BlockIndex for a feed.
    /// Returns MAX of feed BlockIndex and all participants' profile BlockIndex.
    /// This ensures clients detect when any participant updates their identity.
    /// </summary>
    private async Task<long> GetEffectiveBlockIndex(Feed feed)
    {
        var maxBlockIndex = feed.BlockIndex.Value;

        foreach (var participant in feed.Participants)
        {
            var identity = await this._identityService.RetrieveIdentityAsync(participant.ParticipantPublicAddress);

            if (identity is Profile profile && profile.BlockIndex.Value > maxBlockIndex)
            {
                maxBlockIndex = profile.BlockIndex.Value;
            }
        }

        return maxBlockIndex;
    }

    // ===== Group Feed Query Operations (FEAT-017) =====

    public override async Task<GetGroupMembersResponse> GetGroupMembers(
        GetGroupMembersRequest request,
        ServerCallContext context)
    {
        Console.WriteLine($"[GetGroupMembers] FeedId: {request.FeedId}");

        var feedId = FeedIdHandler.CreateFromString(request.FeedId);
        var response = new GetGroupMembersResponse();

        // Cache-aside pattern: Try cache first
        var cachedMembers = await this._groupMembersCacheService.GetGroupMembersAsync(feedId);

        if (cachedMembers != null)
        {
            Console.WriteLine($"[GetGroupMembers] Cache hit - {cachedMembers.Members.Count} members");

            // Convert cached members to proto response
            foreach (var member in cachedMembers.Members)
            {
                var memberProto = new GroupFeedMemberProto
                {
                    PublicAddress = member.PublicAddress,
                    ParticipantType = member.ParticipantType,
                    JoinedAtBlock = member.JoinedAtBlock,
                    DisplayName = member.DisplayName
                };

                if (member.LeftAtBlock != null)
                {
                    memberProto.LeftAtBlock = member.LeftAtBlock.Value;
                }

                response.Members.Add(memberProto);
            }

            return response;
        }

        Console.WriteLine($"[GetGroupMembers] Cache miss - fetching from DB");

        // Cache miss: Get from database and resolve display names
        // Use GetActiveParticipantsAsync to only return current members (excludes left/banned)
        var allParticipants = await this._feedsStorageService.GetActiveParticipantsAsync(feedId);

        var membersToCache = new List<CachedGroupMember>();

        foreach (var participant in allParticipants)
        {
            // Get display name for each participant (uses identity cache internally)
            var displayName = await this.ExtractDisplayName(participant.ParticipantPublicAddress);

            var memberProto = new GroupFeedMemberProto
            {
                PublicAddress = participant.ParticipantPublicAddress,
                ParticipantType = (int)participant.ParticipantType,
                JoinedAtBlock = participant.JoinedAtBlock.Value,
                DisplayName = displayName
            };

            // Include LeftAtBlock if the member has left
            if (participant.LeftAtBlock != null)
            {
                memberProto.LeftAtBlock = participant.LeftAtBlock.Value;
            }

            response.Members.Add(memberProto);

            // Build cache entry
            membersToCache.Add(new CachedGroupMember(
                participant.ParticipantPublicAddress,
                displayName,
                (int)participant.ParticipantType,
                participant.JoinedAtBlock.Value,
                participant.LeftAtBlock?.Value));
        }

        // Populate cache for future requests
        await this._groupMembersCacheService.SetGroupMembersAsync(feedId, new CachedGroupMembers(membersToCache));

        Console.WriteLine($"[GetGroupMembers] Returning {response.Members.Count} members (cached for future requests)");
        return response;
    }

    public override async Task<GetKeyGenerationsResponse> GetKeyGenerations(
        GetKeyGenerationsRequest request,
        ServerCallContext context)
    {
        var userAddress = request.UserPublicAddress ?? string.Empty;
        Console.WriteLine($"[GetKeyGenerations] FeedId: {request.FeedId}, UserAddress: {userAddress.Substring(0, Math.Min(10, userAddress.Length))}...");

        var feedId = FeedIdHandler.CreateFromString(request.FeedId);
        var response = new GetKeyGenerationsResponse();

        // FEAT-050: Try cache first for key generations
        var cachedKeyGenerations = await this._feedParticipantsCacheService.GetKeyGenerationsAsync(feedId);
        if (cachedKeyGenerations != null)
        {
            Console.WriteLine($"[GetKeyGenerations] Cache hit, processing {cachedKeyGenerations.KeyGenerations.Count} key generations");

            // Filter cached key generations for this user
            foreach (var cachedKg in cachedKeyGenerations.KeyGenerations)
            {
                // Check if this user has an encrypted key for this generation
                if (!cachedKg.EncryptedKeysByMember.TryGetValue(userAddress, out var userEncryptedKey))
                {
                    // User doesn't have a key for this generation (wasn't a member)
                    continue;
                }

                var keyGenProto = new KeyGenerationProto
                {
                    KeyGeneration = cachedKg.Version,
                    EncryptedKey = userEncryptedKey,
                    ValidFromBlock = cachedKg.ValidFromBlock
                };

                // ValidToBlock is optional - only set if present
                if (cachedKg.ValidToBlock.HasValue)
                {
                    keyGenProto.ValidToBlock = cachedKg.ValidToBlock.Value;
                }

                response.KeyGenerations.Add(keyGenProto);
            }

            Console.WriteLine($"[GetKeyGenerations] Returning {response.KeyGenerations.Count} key generations for user (from cache)");
            return response;
        }

        Console.WriteLine($"[GetKeyGenerations] Cache miss, querying database");

        // Cache miss - query ALL key generations from database and populate cache
        var allKeyGenerations = await this._feedsStorageService.GetAllKeyGenerationsAsync(feedId);

        if (allKeyGenerations.Count > 0)
        {
            // Convert to cache DTOs and populate cache (fire-and-forget)
            // ValidToBlock is the ValidFromBlock of the next generation, or null for the latest
            var keyGenList = allKeyGenerations.ToList();
            var cacheDtos = new List<KeyGenerationCacheDto>();
            for (int i = 0; i < keyGenList.Count; i++)
            {
                var kg = keyGenList[i];
                var validToBlock = (i < keyGenList.Count - 1) ? keyGenList[i + 1].ValidFromBlock.Value : (long?)null;

                cacheDtos.Add(new KeyGenerationCacheDto
                {
                    Version = kg.KeyGeneration,
                    ValidFromBlock = kg.ValidFromBlock.Value,
                    ValidToBlock = validToBlock,
                    EncryptedKeysByMember = kg.EncryptedKeys.ToDictionary(
                        ek => ek.MemberPublicAddress,
                        ek => ek.EncryptedAesKey)
                });
            }

            var cacheWrapper = new CachedKeyGenerations { KeyGenerations = cacheDtos };
            _ = this._feedParticipantsCacheService.SetKeyGenerationsAsync(feedId, cacheWrapper);
        }

        // Filter and return key generations for this user
        foreach (var kg in allKeyGenerations)
        {
            // Find this user's encrypted key for this KeyGeneration
            var userEncryptedKey = kg.EncryptedKeys
                .FirstOrDefault(ek => ek.MemberPublicAddress == userAddress)
                ?.EncryptedAesKey ?? string.Empty;

            if (string.IsNullOrEmpty(userEncryptedKey))
            {
                // User doesn't have a key for this generation
                continue;
            }

            var keyGenProto = new KeyGenerationProto
            {
                KeyGeneration = kg.KeyGeneration,
                EncryptedKey = userEncryptedKey,
                ValidFromBlock = kg.ValidFromBlock.Value
            };

            // ValidToBlock is optional - only set if there's a newer KeyGeneration
            // For the last key, ValidToBlock remains unset (null)
            response.KeyGenerations.Add(keyGenProto);
        }

        Console.WriteLine($"[GetKeyGenerations] Returning {response.KeyGenerations.Count} key generations");
        return response;
    }

    // ===== Group Feed Info Operation =====

    public override async Task<GetGroupFeedResponse> GetGroupFeed(
        GetGroupFeedRequest request,
        ServerCallContext context)
    {
        Console.WriteLine($"[GetGroupFeed] FeedId: {request.FeedId}");

        try
        {
            var feedId = FeedIdHandler.CreateFromString(request.FeedId);
            var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);

            if (groupFeed == null)
            {
                return new GetGroupFeedResponse
                {
                    Success = false,
                    Message = "Group feed not found"
                };
            }

            // Auto-generate invite code for public groups if not present
            var inviteCode = groupFeed.InviteCode;
            if (groupFeed.IsPublic && string.IsNullOrEmpty(inviteCode))
            {
                inviteCode = await this._feedsStorageService.GenerateInviteCodeAsync(feedId);
                Console.WriteLine($"[GetGroupFeed] Generated invite code: {inviteCode}");
            }

            var response = new GetGroupFeedResponse
            {
                Success = true,
                FeedId = groupFeed.FeedId.ToString(),
                Title = groupFeed.Title,
                Description = groupFeed.Description ?? "",
                IsPublic = groupFeed.IsPublic
            };

            // Only include invite code for public groups
            if (groupFeed.IsPublic && !string.IsNullOrEmpty(inviteCode))
            {
                response.InviteCode = inviteCode;
            }

            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GetGroupFeed] ERROR: {ex.Message}");
            return new GetGroupFeedResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<GetGroupFeedByInviteCodeResponse> GetGroupFeedByInviteCode(
        GetGroupFeedByInviteCodeRequest request,
        ServerCallContext context)
    {
        Console.WriteLine($"[GetGroupFeedByInviteCode] InviteCode: {request.InviteCode}");

        try
        {
            if (string.IsNullOrWhiteSpace(request.InviteCode))
            {
                return new GetGroupFeedByInviteCodeResponse
                {
                    Success = false,
                    Message = "Invite code is required"
                };
            }

            var groupFeed = await this._feedsStorageService.GetGroupFeedByInviteCodeAsync(request.InviteCode);

            if (groupFeed == null)
            {
                return new GetGroupFeedByInviteCodeResponse
                {
                    Success = false,
                    Message = "No public group found with this invite code"
                };
            }

            // Count active members
            var activeParticipants = await this._feedsStorageService.GetActiveParticipantsAsync(groupFeed.FeedId);

            return new GetGroupFeedByInviteCodeResponse
            {
                Success = true,
                FeedId = groupFeed.FeedId.ToString(),
                Title = groupFeed.Title,
                Description = groupFeed.Description ?? "",
                IsPublic = groupFeed.IsPublic,
                MemberCount = activeParticipants.Count
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GetGroupFeedByInviteCode] ERROR: {ex.Message}");
            return new GetGroupFeedByInviteCodeResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    // ===== Group Feed Creation & Membership Operations =====

    public override async Task<NewGroupFeedResponse> CreateGroupFeed(
        NewGroupFeedRequest request,
        ServerCallContext context)
    {
        Console.WriteLine($"[CreateGroupFeed] FeedId: {request.FeedId}, Title: {request.Title}, IsPublic: {request.IsPublic}");

        try
        {
            var feedId = FeedIdHandler.CreateFromString(request.FeedId);

            // Check if feed already exists
            var existingFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
            if (existingFeed != null)
            {
                return new NewGroupFeedResponse
                {
                    Success = false,
                    Message = "A group with this ID already exists"
                };
            }

            var currentBlock = this._blockchainCache.LastBlockIndex;

            // Create the GroupFeed entity
            var groupFeed = new GroupFeed(
                feedId,
                request.Title,
                request.Description ?? string.Empty,
                request.IsPublic,
                currentBlock,
                CurrentKeyGeneration: 0);

            await this._feedsStorageService.CreateGroupFeed(groupFeed);

            // Add participants from request
            foreach (var participant in request.Participants)
            {
                var participantType = (ParticipantType)participant.ParticipantType;
                var participantEntity = new GroupFeedParticipantEntity(
                    feedId,
                    participant.ParticipantPublicAddress,
                    participantType,
                    currentBlock,
                    LeftAtBlock: null,
                    LastLeaveBlock: null);

                await this._feedsStorageService.AddParticipantAsync(feedId, participantEntity);
            }

            // Create initial KeyGeneration (KeyGen 0) with the provided encrypted keys
            var encryptedKeys = request.Participants
                .Select(p => new GroupFeedEncryptedKeyEntity(
                    feedId,
                    KeyGeneration: 0,
                    p.ParticipantPublicAddress,
                    p.EncryptedFeedKey))
                .ToList();

            var keyGeneration = new GroupFeedKeyGenerationEntity(
                feedId,
                KeyGeneration: 0,
                currentBlock,
                RotationTrigger.Join)  // Use Join for initial creation
            {
                EncryptedKeys = encryptedKeys
            };

            await this._feedsStorageService.CreateKeyRotationAsync(keyGeneration);

            // Create the Feed record (links to standard feeds system)
            var feed = new Feed(
                feedId,
                request.Title,
                FeedType.Group,
                currentBlock);

            // Add owner as feed participant
            var ownerParticipantProto = request.Participants.FirstOrDefault(p => p.ParticipantType == (int)ParticipantType.Owner);
            if (ownerParticipantProto != null)
            {
                feed.Participants.Add(new FeedParticipant(
                    feedId,
                    ownerParticipantProto.ParticipantPublicAddress,
                    ParticipantType.Owner,
                    ownerParticipantProto.EncryptedFeedKey)
                {
                    Feed = feed
                });
            }

            await this._feedsStorageService.CreateFeed(feed);

            Console.WriteLine($"[CreateGroupFeed] Success - created group with {request.Participants.Count} participants");
            return new NewGroupFeedResponse
            {
                Success = true,
                Message = "Group created successfully"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CreateGroupFeed] ERROR: {ex.Message}");
            Console.WriteLine($"[CreateGroupFeed] Stack trace: {ex.StackTrace}");
            return new NewGroupFeedResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<JoinGroupFeedResponse> JoinGroupFeed(
        JoinGroupFeedRequest request,
        ServerCallContext context)
    {
        var userAddressLog = request.JoiningUserPublicAddress?.Substring(0, Math.Min(10, request.JoiningUserPublicAddress?.Length ?? 0)) ?? "";
        var hasEncryptKey = request.HasJoiningUserPublicEncryptKey;
        Console.WriteLine($"[JoinGroupFeed] FeedId: {request.FeedId}, User: {userAddressLog}..., HasEncryptKey: {hasEncryptKey}");

        try
        {
            var feedId = FeedIdHandler.CreateFromString(request.FeedId);
            var userAddress = request.JoiningUserPublicAddress ?? string.Empty;
            var userEncryptKey = request.HasJoiningUserPublicEncryptKey ? request.JoiningUserPublicEncryptKey : null;

            // Check if group exists, is not deleted, and is public
            var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
            if (groupFeed == null)
            {
                return new JoinGroupFeedResponse
                {
                    Success = false,
                    Message = "Group feed not found"
                };
            }

            if (groupFeed.IsDeleted)
            {
                return new JoinGroupFeedResponse
                {
                    Success = false,
                    Message = "This group has been deleted"
                };
            }

            if (!groupFeed.IsPublic)
            {
                return new JoinGroupFeedResponse
                {
                    Success = false,
                    Message = "Cannot join private group. An admin must add you."
                };
            }

            // Check if user is already a member
            var existingParticipant = await this._feedsStorageService.GetParticipantWithHistoryAsync(feedId, userAddress);
            if (existingParticipant != null && existingParticipant.LeftAtBlock == null)
            {
                return new JoinGroupFeedResponse
                {
                    Success = false,
                    Message = "You are already a member of this group"
                };
            }

            // Check for cooldown period (100 blocks after leaving)
            if (existingParticipant?.LastLeaveBlock != null)
            {
                var currentBlock = this._blockchainCache.LastBlockIndex.Value;
                var cooldownEnd = existingParticipant.LastLeaveBlock.Value + 100;
                if (currentBlock < cooldownEnd)
                {
                    return new JoinGroupFeedResponse
                    {
                        Success = false,
                        Message = $"Cannot rejoin until block {cooldownEnd}. You left at block {existingParticipant.LastLeaveBlock.Value}."
                    };
                }
            }

            var joinedAtBlock = this._blockchainCache.LastBlockIndex;

            // Add or update participant
            if (existingParticipant != null)
            {
                await this._feedsStorageService.UpdateParticipantRejoinAsync(
                    feedId,
                    userAddress,
                    joinedAtBlock,
                    ParticipantType.Member);
            }
            else
            {
                var newParticipant = new GroupFeedParticipantEntity(
                    feedId,
                    userAddress,
                    ParticipantType.Member,
                    joinedAtBlock,
                    LeftAtBlock: null,
                    LastLeaveBlock: null);

                await this._feedsStorageService.AddParticipantAsync(feedId, newParticipant);
            }

            // Trigger key rotation - pass the user's encrypt key if provided (avoids identity lookup timing issue)
            var (success, newKeyGen, errorMsg) = await TriggerKeyRotationAsync(
                feedId,
                RotationTrigger.Join,
                joiningMemberAddress: userAddress,
                leavingMemberAddress: null,
                joiningMemberPublicEncryptKey: userEncryptKey);

            if (!success)
            {
                return new JoinGroupFeedResponse
                {
                    Success = false,
                    Message = $"Joined group but key distribution failed: {errorMsg}"
                };
            }

            await this._feedsStorageService.UpdateFeedBlockIndexAsync(feedId, joinedAtBlock);

            // CRITICAL: Invalidate KeyGenerations cache SYNCHRONOUSLY before returning
            // This prevents race condition where client queries cache before new keys are visible
            await this._feedParticipantsCacheService.InvalidateKeyGenerationsAsync(feedId);
            Console.WriteLine($"[JoinGroupFeed] Success - new KeyGeneration: {newKeyGen}, cache invalidated");

            return new JoinGroupFeedResponse
            {
                Success = true,
                Message = "Successfully joined the group"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[JoinGroupFeed] ERROR: {ex.Message}");
            return new JoinGroupFeedResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<LeaveGroupFeedResponse> LeaveGroupFeed(
        LeaveGroupFeedRequest request,
        ServerCallContext context)
    {
        Console.WriteLine($"[LeaveGroupFeed] FeedId: {request.FeedId}, User: {request.LeavingUserPublicAddress?.Substring(0, Math.Min(10, request.LeavingUserPublicAddress?.Length ?? 0))}...");

        try
        {
            var feedId = FeedIdHandler.CreateFromString(request.FeedId);
            var userAddress = request.LeavingUserPublicAddress ?? string.Empty;

            // Check if group is deleted - all actions are frozen after deletion
            var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
            if (groupFeed == null)
            {
                return new LeaveGroupFeedResponse
                {
                    Success = false,
                    Message = "Group not found"
                };
            }

            if (groupFeed.IsDeleted)
            {
                return new LeaveGroupFeedResponse
                {
                    Success = false,
                    Message = "This group has been deleted. All actions are frozen."
                };
            }

            // Check if user is a member
            var participant = await this._feedsStorageService.GetParticipantWithHistoryAsync(feedId, userAddress);
            if (participant == null || participant.LeftAtBlock != null)
            {
                return new LeaveGroupFeedResponse
                {
                    Success = false,
                    Message = "You are not a member of this group"
                };
            }

            // Check if user is the last admin
            if (participant.ParticipantType == ParticipantType.Owner || participant.ParticipantType == ParticipantType.Admin)
            {
                var adminCount = await this._feedsStorageService.GetAdminCountAsync(feedId);
                if (adminCount <= 1)
                {
                    return new LeaveGroupFeedResponse
                    {
                        Success = false,
                        Message = "Cannot leave: you are the last admin. Promote another member first or delete the group."
                    };
                }
            }

            var leftAtBlock = this._blockchainCache.LastBlockIndex;

            // Update participant status
            await this._feedsStorageService.UpdateParticipantLeaveStatusAsync(feedId, userAddress, leftAtBlock);

            // Trigger key rotation (exclude leaving member)
            var (success, newKeyGen, errorMsg) = await TriggerKeyRotationAsync(
                feedId,
                RotationTrigger.Leave,
                leavingMemberAddress: userAddress);

            if (!success)
            {
                Console.WriteLine($"[LeaveGroupFeed] Key rotation failed: {errorMsg}");
                // Continue anyway - the user has left, key rotation is best-effort
            }

            await this._feedsStorageService.UpdateFeedBlockIndexAsync(feedId, leftAtBlock);

            // CRITICAL: Invalidate KeyGenerations cache SYNCHRONOUSLY
            await this._feedParticipantsCacheService.InvalidateKeyGenerationsAsync(feedId);
            Console.WriteLine($"[LeaveGroupFeed] Success, cache invalidated");

            return new LeaveGroupFeedResponse
            {
                Success = true,
                Message = "Successfully left the group"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LeaveGroupFeed] ERROR: {ex.Message}");
            return new LeaveGroupFeedResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    // ===== Group Feed Admin Operations (FEAT-017) =====

    public override async Task<AddMemberToGroupFeedResponse> AddMemberToGroupFeed(
        AddMemberToGroupFeedRequest request,
        ServerCallContext context)
    {
        Console.WriteLine($"[AddMemberToGroupFeed] FeedId: {request.FeedId}, Admin: {request.AdminPublicAddress?.Substring(0, Math.Min(10, request.AdminPublicAddress?.Length ?? 0))}..., NewMember: {request.NewMemberPublicAddress?.Substring(0, Math.Min(10, request.NewMemberPublicAddress?.Length ?? 0))}...");

        try
        {
            var feedId = FeedIdHandler.CreateFromString(request.FeedId);
            var adminAddress = request.AdminPublicAddress ?? string.Empty;
            var newMemberAddress = request.NewMemberPublicAddress ?? string.Empty;
            var newMemberEncryptKey = request.NewMemberPublicEncryptKey ?? string.Empty;

            // Step 0: Check if group is deleted - all actions are frozen after deletion
            var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
            if (groupFeed == null)
            {
                return new AddMemberToGroupFeedResponse
                {
                    Success = false,
                    Message = "Group not found"
                };
            }

            if (groupFeed.IsDeleted)
            {
                return new AddMemberToGroupFeedResponse
                {
                    Success = false,
                    Message = "This group has been deleted. All actions are frozen."
                };
            }

            // Step 1: Validate admin has permission
            var isAdmin = await this._feedsStorageService.IsAdminAsync(feedId, adminAddress);
            if (!isAdmin)
            {
                Console.WriteLine($"[AddMemberToGroupFeed] Failed: User is not an admin");
                return new AddMemberToGroupFeedResponse
                {
                    Success = false,
                    Message = "Only administrators can add members to the group"
                };
            }

            // Step 2: Check if member is already in the group
            var existingParticipant = await this._feedsStorageService.GetParticipantWithHistoryAsync(feedId, newMemberAddress);
            if (existingParticipant != null && existingParticipant.LeftAtBlock == null)
            {
                Console.WriteLine($"[AddMemberToGroupFeed] Failed: Member already in group");
                return new AddMemberToGroupFeedResponse
                {
                    Success = false,
                    Message = "User is already a member of this group"
                };
            }

            // Step 3: Get current block height for JoinedAtBlock
            var currentBlock = this._blockchainCache.LastBlockIndex;
            var joinedAtBlock = currentBlock;

            // Step 4: Add or update the participant
            if (existingParticipant != null)
            {
                // Participant existed before (re-joining after leaving) - update their status
                await this._feedsStorageService.UpdateParticipantRejoinAsync(
                    feedId,
                    newMemberAddress,
                    joinedAtBlock,
                    ParticipantType.Member);
                Console.WriteLine($"[AddMemberToGroupFeed] Updated existing participant for rejoin");
            }
            else
            {
                // New participant - create new record
                var newParticipant = new GroupFeedParticipantEntity(
                    feedId,
                    newMemberAddress,
                    ParticipantType.Member,
                    joinedAtBlock,
                    LeftAtBlock: null,
                    LastLeaveBlock: null);

                await this._feedsStorageService.AddParticipantAsync(feedId, newParticipant);
                Console.WriteLine($"[AddMemberToGroupFeed] Added new participant");
            }

            // Step 5: Trigger key rotation to include the new member
            Console.WriteLine($"[AddMemberToGroupFeed] Triggering key rotation for new member");
            var rotationResult = await TriggerKeyRotationAsync(
                feedId,
                RotationTrigger.Join,
                joiningMemberAddress: newMemberAddress);
            var (rotationSuccess, newKeyGeneration, errorMessage) = rotationResult;

            if (!rotationSuccess)
            {
                Console.WriteLine($"[AddMemberToGroupFeed] Key rotation failed: {errorMessage}");
                // Note: Member was added but key rotation failed. This is a partial failure.
                // The member won't be able to decrypt messages until key rotation succeeds.
                return new AddMemberToGroupFeedResponse
                {
                    Success = false,
                    Message = $"Member was added but key distribution failed: {errorMessage}"
                };
            }

            // Step 6: Update the feed's BlockIndex to signal other clients that there's a change
            // This ensures existing group members will sync the new KeyGeneration
            Console.WriteLine($"[AddMemberToGroupFeed] Updating feed BlockIndex to trigger client sync");
            await this._feedsStorageService.UpdateFeedBlockIndexAsync(feedId, currentBlock);

            // CRITICAL: Invalidate KeyGenerations cache SYNCHRONOUSLY
            await this._feedParticipantsCacheService.InvalidateKeyGenerationsAsync(feedId);
            Console.WriteLine($"[AddMemberToGroupFeed] Success - new KeyGeneration: {newKeyGeneration}, cache invalidated");

            return new AddMemberToGroupFeedResponse
            {
                Success = true,
                Message = $"Member added successfully. New key generation: {newKeyGeneration}"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AddMemberToGroupFeed] ERROR: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[AddMemberToGroupFeed] Stack trace: {ex.StackTrace}");
            return new AddMemberToGroupFeedResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Triggers a key rotation for a Group Feed when a member joins.
    /// Generates a new AES key, encrypts it for all current active members, and stores the key generation.
    /// </summary>
    /// <param name="joiningMemberPublicEncryptKey">Optional: The public encrypt key for the joining member.
    /// When provided, skips identity lookup for the joining member (avoids timing issues when identity isn't confirmed yet).</param>
    private async Task<(bool Success, int? NewKeyGeneration, string? ErrorMessage)> TriggerKeyRotationAsync(
        FeedId feedId,
        RotationTrigger trigger,
        string? joiningMemberAddress = null,
        string? leavingMemberAddress = null,
        string? joiningMemberPublicEncryptKey = null)
    {
        // Step 1: Get current max KeyGeneration
        var currentMaxKeyGeneration = await this._feedsStorageService.GetMaxKeyGenerationAsync(feedId);
        if (currentMaxKeyGeneration == null)
        {
            return (false, null, $"Group feed {feedId} does not exist or has no KeyGenerations.");
        }

        var newKeyGeneration = currentMaxKeyGeneration.Value + 1;

        // Step 2: Get active member addresses (excludes Banned, includes Admin/Member/Blocked)
        var memberAddresses = (await this._feedsStorageService.GetActiveGroupMemberAddressesAsync(feedId)).ToList();

        // Adjust member list based on membership change
        if (!string.IsNullOrEmpty(leavingMemberAddress))
        {
            // For Leave/Ban: exclude the leaving/banned member
            memberAddresses = memberAddresses.Where(a => a != leavingMemberAddress).ToList();
        }

        if (!string.IsNullOrEmpty(joiningMemberAddress))
        {
            // For Join/Unban: include the joining member (might already be in list for Unban)
            if (!memberAddresses.Contains(joiningMemberAddress))
            {
                memberAddresses.Add(joiningMemberAddress);
            }
        }

        // Step 3: Validate member count
        if (memberAddresses.Count == 0)
        {
            return (false, null, "Cannot rotate keys for a group with no active members.");
        }

        if (memberAddresses.Count > MaxMembersPerRotation)
        {
            return (false, null, $"Group has {memberAddresses.Count} members, exceeding the maximum of {MaxMembersPerRotation}.");
        }

        // Step 4: Generate new AES-256 key
        var plaintextAesKey = EncryptKeys.GenerateAesKey();

        // Step 5: Encrypt the AES key for each member using ECIES
        var encryptedKeys = new List<GroupFeedEncryptedKeyEntity>();
        try
        {
            foreach (var memberAddress in memberAddresses)
            {
                string publicEncryptKey;

                // Check if this is the joining member and we have their key provided directly
                if (!string.IsNullOrEmpty(joiningMemberPublicEncryptKey) &&
                    memberAddress == joiningMemberAddress)
                {
                    // Use the provided key directly (avoids timing issue where identity isn't in DB yet)
                    publicEncryptKey = joiningMemberPublicEncryptKey;
                    Console.WriteLine($"[TriggerKeyRotationAsync] Using provided public encrypt key for joining member {memberAddress.Substring(0, Math.Min(10, memberAddress.Length))}...");
                }
                else
                {
                    // Fetch the member's public encrypt key from Identity module
                    var profile = await this._identityStorageService.RetrieveIdentityAsync(memberAddress);

                    if (profile is NonExistingProfile || profile is not Profile fullProfile)
                    {
                        return (false, null, $"Could not retrieve identity for member {memberAddress}. Cannot complete key rotation.");
                    }

                    // Validate public encrypt key before attempting encryption
                    if (string.IsNullOrEmpty(fullProfile.PublicEncryptAddress))
                    {
                        return (false, null, $"Member {memberAddress} has an empty public encrypt key. Cannot complete key rotation.");
                    }

                    publicEncryptKey = fullProfile.PublicEncryptAddress;
                }

                // ECIES encrypt the AES key with the member's public encrypt key
                string encryptedAesKey;
                try
                {
                    encryptedAesKey = EncryptKeys.Encrypt(plaintextAesKey, publicEncryptKey);
                }
                catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentException)
                {
                    return (false, null, $"ECIES encryption failed for member {memberAddress}: invalid public key format. Cannot complete key rotation.");
                }

                encryptedKeys.Add(new GroupFeedEncryptedKeyEntity(
                    FeedId: feedId,
                    KeyGeneration: newKeyGeneration,
                    MemberPublicAddress: memberAddress,
                    EncryptedAesKey: encryptedAesKey));
            }
        }
        finally
        {
            // Step 6: Security - zero the plaintext key from memory
            // Note: In .NET, strings are immutable and cannot be truly zeroed.
            plaintextAesKey = null!;
        }

        // Step 7: Create and store the KeyGeneration
        var validFromBlock = this._blockchainCache.LastBlockIndex.Value;
        var keyGenerationEntity = new GroupFeedKeyGenerationEntity(
            FeedId: feedId,
            KeyGeneration: newKeyGeneration,
            ValidFromBlock: new BlockIndex(validFromBlock),
            RotationTrigger: trigger)
        {
            EncryptedKeys = encryptedKeys
        };

        await this._feedsStorageService.CreateKeyRotationAsync(keyGenerationEntity);

        return (true, newKeyGeneration, null);
    }

    public override async Task<BlockMemberResponse> BlockMember(
        BlockMemberRequest request,
        ServerCallContext context)
    {
        Console.WriteLine($"[BlockMember] FeedId: {request.FeedId}, Admin: {request.AdminPublicAddress?.Substring(0, Math.Min(10, request.AdminPublicAddress?.Length ?? 0))}..., Blocked: {request.BlockedUserPublicAddress?.Substring(0, Math.Min(10, request.BlockedUserPublicAddress?.Length ?? 0))}...");

        try
        {
            var feedId = FeedIdHandler.CreateFromString(request.FeedId);
            var adminAddress = request.AdminPublicAddress ?? string.Empty;
            var blockedUserAddress = request.BlockedUserPublicAddress ?? string.Empty;

            // Step 0: Check if group is deleted - all actions are frozen after deletion
            var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
            if (groupFeed == null)
            {
                return new BlockMemberResponse
                {
                    Success = false,
                    Message = "Group not found"
                };
            }

            if (groupFeed.IsDeleted)
            {
                return new BlockMemberResponse
                {
                    Success = false,
                    Message = "This group has been deleted. All actions are frozen."
                };
            }

            // Step 1: Validate admin has permission
            var isAdmin = await this._feedsStorageService.IsAdminAsync(feedId, adminAddress);
            if (!isAdmin)
            {
                return new BlockMemberResponse
                {
                    Success = false,
                    Message = "Only administrators can block members"
                };
            }

            // Step 2: Check if target user is a member
            var targetParticipant = await this._feedsStorageService.GetParticipantWithHistoryAsync(feedId, blockedUserAddress);
            if (targetParticipant == null || targetParticipant.LeftAtBlock != null)
            {
                return new BlockMemberResponse
                {
                    Success = false,
                    Message = "User is not an active member of this group"
                };
            }

            // Step 3: Cannot block admins/owners
            if (targetParticipant.ParticipantType == ParticipantType.Admin ||
                targetParticipant.ParticipantType == ParticipantType.Owner)
            {
                return new BlockMemberResponse
                {
                    Success = false,
                    Message = "Cannot block an administrator. Demote them first."
                };
            }

            // Step 4: Update participant status to Blocked
            await this._feedsStorageService.UpdateParticipantTypeAsync(
                feedId,
                blockedUserAddress,
                ParticipantType.Blocked);

            // Note: Blocking does NOT trigger key rotation - blocked users can still decrypt messages
            // They just cannot send new messages. Use Ban for cryptographic exclusion.

            Console.WriteLine($"[BlockMember] Success");
            return new BlockMemberResponse
            {
                Success = true,
                Message = "Member blocked successfully"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BlockMember] ERROR: {ex.Message}");
            return new BlockMemberResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<UnblockMemberResponse> UnblockMember(
        UnblockMemberRequest request,
        ServerCallContext context)
    {
        Console.WriteLine($"[UnblockMember] FeedId: {request.FeedId}, Admin: {request.AdminPublicAddress?.Substring(0, Math.Min(10, request.AdminPublicAddress?.Length ?? 0))}..., Unblocked: {request.UnblockedUserPublicAddress?.Substring(0, Math.Min(10, request.UnblockedUserPublicAddress?.Length ?? 0))}...");

        try
        {
            var feedId = FeedIdHandler.CreateFromString(request.FeedId);
            var adminAddress = request.AdminPublicAddress ?? string.Empty;
            var unblockedUserAddress = request.UnblockedUserPublicAddress ?? string.Empty;

            // Step 0: Check if group is deleted - all actions are frozen after deletion
            var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
            if (groupFeed == null)
            {
                return new UnblockMemberResponse
                {
                    Success = false,
                    Message = "Group not found"
                };
            }

            if (groupFeed.IsDeleted)
            {
                return new UnblockMemberResponse
                {
                    Success = false,
                    Message = "This group has been deleted. All actions are frozen."
                };
            }

            // Step 1: Validate admin has permission
            var isAdmin = await this._feedsStorageService.IsAdminAsync(feedId, adminAddress);
            if (!isAdmin)
            {
                return new UnblockMemberResponse
                {
                    Success = false,
                    Message = "Only administrators can unblock members"
                };
            }

            // Step 2: Check if target user is blocked
            var targetParticipant = await this._feedsStorageService.GetParticipantWithHistoryAsync(feedId, unblockedUserAddress);
            if (targetParticipant == null || targetParticipant.ParticipantType != ParticipantType.Blocked)
            {
                return new UnblockMemberResponse
                {
                    Success = false,
                    Message = "User is not blocked in this group"
                };
            }

            // Step 3: Update participant status back to Member
            await this._feedsStorageService.UpdateParticipantTypeAsync(
                feedId,
                unblockedUserAddress,
                ParticipantType.Member);

            Console.WriteLine($"[UnblockMember] Success");
            return new UnblockMemberResponse
            {
                Success = true,
                Message = "Member unblocked successfully"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UnblockMember] ERROR: {ex.Message}");
            return new UnblockMemberResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<BanFromGroupFeedResponse> BanFromGroupFeed(
        BanFromGroupFeedRequest request,
        ServerCallContext context)
    {
        Console.WriteLine($"[BanFromGroupFeed] FeedId: {request.FeedId}, Admin: {request.AdminPublicAddress?.Substring(0, Math.Min(10, request.AdminPublicAddress?.Length ?? 0))}..., Banned: {request.BannedUserPublicAddress?.Substring(0, Math.Min(10, request.BannedUserPublicAddress?.Length ?? 0))}...");

        try
        {
            var feedId = FeedIdHandler.CreateFromString(request.FeedId);
            var adminAddress = request.AdminPublicAddress ?? string.Empty;
            var bannedUserAddress = request.BannedUserPublicAddress ?? string.Empty;

            // Step 0: Check if group is deleted - all actions are frozen after deletion
            var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
            if (groupFeed == null)
            {
                return new BanFromGroupFeedResponse
                {
                    Success = false,
                    Message = "Group not found"
                };
            }

            if (groupFeed.IsDeleted)
            {
                return new BanFromGroupFeedResponse
                {
                    Success = false,
                    Message = "This group has been deleted. All actions are frozen."
                };
            }

            // Step 1: Validate admin has permission
            var isAdmin = await this._feedsStorageService.IsAdminAsync(feedId, adminAddress);
            if (!isAdmin)
            {
                return new BanFromGroupFeedResponse
                {
                    Success = false,
                    Message = "Only administrators can ban members"
                };
            }

            // Step 2: Check if target user is a member
            var targetParticipant = await this._feedsStorageService.GetParticipantWithHistoryAsync(feedId, bannedUserAddress);
            if (targetParticipant == null || targetParticipant.LeftAtBlock != null)
            {
                return new BanFromGroupFeedResponse
                {
                    Success = false,
                    Message = "User is not an active member of this group"
                };
            }

            // Step 3: Cannot ban admins/owners
            if (targetParticipant.ParticipantType == ParticipantType.Admin ||
                targetParticipant.ParticipantType == ParticipantType.Owner)
            {
                return new BanFromGroupFeedResponse
                {
                    Success = false,
                    Message = "Cannot ban an administrator. Demote them first."
                };
            }

            // Step 4: Update participant status to Banned (marks them as left)
            var bannedAtBlock = this._blockchainCache.LastBlockIndex;
            await this._feedsStorageService.UpdateParticipantBanAsync(feedId, bannedUserAddress, bannedAtBlock);

            // Step 5: Trigger key rotation to exclude banned user
            var (success, newKeyGen, errorMsg) = await TriggerKeyRotationAsync(
                feedId,
                RotationTrigger.Ban,
                leavingMemberAddress: bannedUserAddress);

            if (!success)
            {
                Console.WriteLine($"[BanFromGroupFeed] Key rotation failed: {errorMsg}");
            }

            await this._feedsStorageService.UpdateFeedBlockIndexAsync(feedId, bannedAtBlock);

            // CRITICAL: Invalidate KeyGenerations cache SYNCHRONOUSLY
            await this._feedParticipantsCacheService.InvalidateKeyGenerationsAsync(feedId);
            Console.WriteLine($"[BanFromGroupFeed] Success - new KeyGeneration: {newKeyGen}, cache invalidated");

            return new BanFromGroupFeedResponse
            {
                Success = true,
                Message = "Member banned successfully"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BanFromGroupFeed] ERROR: {ex.Message}");
            return new BanFromGroupFeedResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<UnbanFromGroupFeedResponse> UnbanFromGroupFeed(
        UnbanFromGroupFeedRequest request,
        ServerCallContext context)
    {
        Console.WriteLine($"[UnbanFromGroupFeed] FeedId: {request.FeedId}, Admin: {request.AdminPublicAddress?.Substring(0, Math.Min(10, request.AdminPublicAddress?.Length ?? 0))}..., Unbanned: {request.UnbannedUserPublicAddress?.Substring(0, Math.Min(10, request.UnbannedUserPublicAddress?.Length ?? 0))}...");

        try
        {
            var feedId = FeedIdHandler.CreateFromString(request.FeedId);
            var adminAddress = request.AdminPublicAddress ?? string.Empty;
            var unbannedUserAddress = request.UnbannedUserPublicAddress ?? string.Empty;

            // Step 0: Check if group is deleted - all actions are frozen after deletion
            var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
            if (groupFeed == null)
            {
                return new UnbanFromGroupFeedResponse
                {
                    Success = false,
                    Message = "Group not found"
                };
            }

            if (groupFeed.IsDeleted)
            {
                return new UnbanFromGroupFeedResponse
                {
                    Success = false,
                    Message = "This group has been deleted. All actions are frozen."
                };
            }

            // Step 1: Validate admin has permission
            var isAdmin = await this._feedsStorageService.IsAdminAsync(feedId, adminAddress);
            if (!isAdmin)
            {
                return new UnbanFromGroupFeedResponse
                {
                    Success = false,
                    Message = "Only administrators can unban members"
                };
            }

            // Step 2: Check if target user is banned
            var isBanned = await this._feedsStorageService.IsBannedAsync(feedId, unbannedUserAddress);
            if (!isBanned)
            {
                return new UnbanFromGroupFeedResponse
                {
                    Success = false,
                    Message = "User is not banned from this group"
                };
            }

            // Step 3: Update participant status back to Member and reset timestamps
            var rejoinedAtBlock = this._blockchainCache.LastBlockIndex;
            await this._feedsStorageService.UpdateParticipantUnbanAsync(feedId, unbannedUserAddress, rejoinedAtBlock);

            // Step 4: Trigger key rotation to include unbanned user
            var (success, newKeyGen, errorMsg) = await TriggerKeyRotationAsync(
                feedId,
                RotationTrigger.Unban,
                joiningMemberAddress: unbannedUserAddress);

            if (!success)
            {
                return new UnbanFromGroupFeedResponse
                {
                    Success = false,
                    Message = $"Unbanned but key distribution failed: {errorMsg}"
                };
            }

            await this._feedsStorageService.UpdateFeedBlockIndexAsync(feedId, rejoinedAtBlock);

            // CRITICAL: Invalidate KeyGenerations cache SYNCHRONOUSLY
            await this._feedParticipantsCacheService.InvalidateKeyGenerationsAsync(feedId);
            Console.WriteLine($"[UnbanFromGroupFeed] Success - new KeyGeneration: {newKeyGen}, cache invalidated");

            return new UnbanFromGroupFeedResponse
            {
                Success = true,
                Message = "Member unbanned successfully"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UnbanFromGroupFeed] ERROR: {ex.Message}");
            return new UnbanFromGroupFeedResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<PromoteToAdminResponse> PromoteToAdmin(
        PromoteToAdminRequest request,
        ServerCallContext context)
    {
        Console.WriteLine($"[PromoteToAdmin] FeedId: {request.FeedId}, Admin: {request.AdminPublicAddress?.Substring(0, Math.Min(10, request.AdminPublicAddress?.Length ?? 0))}..., Member: {request.MemberPublicAddress?.Substring(0, Math.Min(10, request.MemberPublicAddress?.Length ?? 0))}...");

        try
        {
            var feedId = FeedIdHandler.CreateFromString(request.FeedId);
            var adminAddress = request.AdminPublicAddress ?? string.Empty;
            var memberAddress = request.MemberPublicAddress ?? string.Empty;

            // Step 0: Check if group is deleted - all actions are frozen after deletion
            var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
            if (groupFeed == null)
            {
                return new PromoteToAdminResponse
                {
                    Success = false,
                    Message = "Group not found"
                };
            }

            if (groupFeed.IsDeleted)
            {
                return new PromoteToAdminResponse
                {
                    Success = false,
                    Message = "This group has been deleted. All actions are frozen."
                };
            }

            // Step 1: Validate requester is admin/owner
            var isAdmin = await this._feedsStorageService.IsAdminAsync(feedId, adminAddress);
            if (!isAdmin)
            {
                return new PromoteToAdminResponse
                {
                    Success = false,
                    Message = "Only administrators can promote members"
                };
            }

            // Step 2: Check if target user is a member
            var targetParticipant = await this._feedsStorageService.GetParticipantWithHistoryAsync(feedId, memberAddress);
            if (targetParticipant == null || targetParticipant.LeftAtBlock != null)
            {
                return new PromoteToAdminResponse
                {
                    Success = false,
                    Message = "User is not an active member of this group"
                };
            }

            // Step 3: Check if already admin
            if (targetParticipant.ParticipantType == ParticipantType.Admin ||
                targetParticipant.ParticipantType == ParticipantType.Owner)
            {
                return new PromoteToAdminResponse
                {
                    Success = false,
                    Message = "User is already an administrator"
                };
            }

            // Step 4: Update participant status to Admin
            await this._feedsStorageService.UpdateParticipantTypeAsync(
                feedId,
                memberAddress,
                ParticipantType.Admin);

            // Update feed BlockIndex to notify clients
            var currentBlock = this._blockchainCache.LastBlockIndex;
            await this._feedsStorageService.UpdateFeedBlockIndexAsync(feedId, currentBlock);

            Console.WriteLine($"[PromoteToAdmin] Success");
            return new PromoteToAdminResponse
            {
                Success = true,
                Message = "Member promoted to administrator"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PromoteToAdmin] ERROR: {ex.Message}");
            return new PromoteToAdminResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<UpdateGroupFeedTitleResponse> UpdateGroupFeedTitle(
        UpdateGroupFeedTitleRequest request,
        ServerCallContext context)
    {
        Console.WriteLine($"[UpdateGroupFeedTitle] FeedId: {request.FeedId}, Admin: {request.AdminPublicAddress?.Substring(0, Math.Min(10, request.AdminPublicAddress?.Length ?? 0))}..., NewTitle: {request.NewTitle}");

        try
        {
            var feedId = FeedIdHandler.CreateFromString(request.FeedId);
            var adminAddress = request.AdminPublicAddress ?? string.Empty;
            var newTitle = request.NewTitle ?? string.Empty;

            // Step 0: Check if group is deleted - all actions are frozen after deletion
            var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
            if (groupFeed == null)
            {
                return new UpdateGroupFeedTitleResponse
                {
                    Success = false,
                    Message = "Group not found"
                };
            }

            if (groupFeed.IsDeleted)
            {
                return new UpdateGroupFeedTitleResponse
                {
                    Success = false,
                    Message = "This group has been deleted. All actions are frozen."
                };
            }

            // Step 1: Validate admin has permission
            var isAdmin = await this._feedsStorageService.IsAdminAsync(feedId, adminAddress);
            if (!isAdmin)
            {
                return new UpdateGroupFeedTitleResponse
                {
                    Success = false,
                    Message = "Only administrators can update the group title"
                };
            }

            // Step 2: Validate title
            if (string.IsNullOrWhiteSpace(newTitle))
            {
                return new UpdateGroupFeedTitleResponse
                {
                    Success = false,
                    Message = "Title cannot be empty"
                };
            }

            if (newTitle.Length > 100)
            {
                return new UpdateGroupFeedTitleResponse
                {
                    Success = false,
                    Message = "Title cannot exceed 100 characters"
                };
            }

            // Step 3: Update the title
            await this._feedsStorageService.UpdateGroupFeedTitleAsync(feedId, newTitle);

            // Update feed BlockIndex to notify clients
            var currentBlock = this._blockchainCache.LastBlockIndex;
            await this._feedsStorageService.UpdateFeedBlockIndexAsync(feedId, currentBlock);

            Console.WriteLine($"[UpdateGroupFeedTitle] Success");
            return new UpdateGroupFeedTitleResponse
            {
                Success = true,
                Message = "Group title updated successfully"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UpdateGroupFeedTitle] ERROR: {ex.Message}");
            return new UpdateGroupFeedTitleResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<UpdateGroupFeedDescriptionResponse> UpdateGroupFeedDescription(
        UpdateGroupFeedDescriptionRequest request,
        ServerCallContext context)
    {
        Console.WriteLine($"[UpdateGroupFeedDescription] FeedId: {request.FeedId}, Admin: {request.AdminPublicAddress?.Substring(0, Math.Min(10, request.AdminPublicAddress?.Length ?? 0))}...");

        try
        {
            var feedId = FeedIdHandler.CreateFromString(request.FeedId);
            var adminAddress = request.AdminPublicAddress ?? string.Empty;
            var newDescription = request.NewDescription ?? string.Empty;

            // Step 0: Check if group is deleted - all actions are frozen after deletion
            var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
            if (groupFeed == null)
            {
                return new UpdateGroupFeedDescriptionResponse
                {
                    Success = false,
                    Message = "Group not found"
                };
            }

            if (groupFeed.IsDeleted)
            {
                return new UpdateGroupFeedDescriptionResponse
                {
                    Success = false,
                    Message = "This group has been deleted. All actions are frozen."
                };
            }

            // Step 1: Validate admin has permission
            var isAdmin = await this._feedsStorageService.IsAdminAsync(feedId, adminAddress);
            if (!isAdmin)
            {
                return new UpdateGroupFeedDescriptionResponse
                {
                    Success = false,
                    Message = "Only administrators can update the group description"
                };
            }

            // Step 2: Validate description
            if (newDescription.Length > 500)
            {
                return new UpdateGroupFeedDescriptionResponse
                {
                    Success = false,
                    Message = "Description cannot exceed 500 characters"
                };
            }

            // Step 3: Update the description
            await this._feedsStorageService.UpdateGroupFeedDescriptionAsync(feedId, newDescription);

            // Update feed BlockIndex to notify clients
            var currentBlock = this._blockchainCache.LastBlockIndex;
            await this._feedsStorageService.UpdateFeedBlockIndexAsync(feedId, currentBlock);

            Console.WriteLine($"[UpdateGroupFeedDescription] Success");
            return new UpdateGroupFeedDescriptionResponse
            {
                Success = true,
                Message = "Group description updated successfully"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UpdateGroupFeedDescription] ERROR: {ex.Message}");
            return new UpdateGroupFeedDescriptionResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<UpdateGroupFeedSettingsResponse> UpdateGroupFeedSettings(
        UpdateGroupFeedSettingsRequest request,
        ServerCallContext context)
    {
        Console.WriteLine($"[UpdateGroupFeedSettings] FeedId: {request.FeedId}, Admin: {request.AdminPublicAddress?.Substring(0, Math.Min(10, request.AdminPublicAddress?.Length ?? 0))}...");

        try
        {
            var feedId = FeedIdHandler.CreateFromString(request.FeedId);
            var adminAddress = request.AdminPublicAddress ?? string.Empty;

            // Step 0: Check if group is deleted - all actions are frozen after deletion
            var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
            if (groupFeed == null)
            {
                return new UpdateGroupFeedSettingsResponse
                {
                    Success = false,
                    Message = "Group not found"
                };
            }

            if (groupFeed.IsDeleted)
            {
                return new UpdateGroupFeedSettingsResponse
                {
                    Success = false,
                    Message = "This group has been deleted. All actions are frozen."
                };
            }

            // Step 1: Validate admin has permission
            var isAdmin = await this._feedsStorageService.IsAdminAsync(feedId, adminAddress);
            if (!isAdmin)
            {
                return new UpdateGroupFeedSettingsResponse
                {
                    Success = false,
                    Message = "Only administrators can update group settings"
                };
            }

            // Step 2: Validate inputs
            string? newTitle = request.HasNewTitle ? request.NewTitle : null;
            string? newDescription = request.HasNewDescription ? request.NewDescription : null;
            bool? isPublic = request.HasIsPublic ? request.IsPublic : null;

            if (newTitle != null && (newTitle.Length < 1 || newTitle.Length > 100))
            {
                return new UpdateGroupFeedSettingsResponse
                {
                    Success = false,
                    Message = "Title must be between 1 and 100 characters"
                };
            }

            if (newDescription != null && newDescription.Length > 500)
            {
                return new UpdateGroupFeedSettingsResponse
                {
                    Success = false,
                    Message = "Description cannot exceed 500 characters"
                };
            }

            // Step 3: Update the settings
            await this._feedsStorageService.UpdateGroupFeedSettingsAsync(feedId, newTitle, newDescription, isPublic);

            // Update feed BlockIndex to notify clients
            var currentBlock = this._blockchainCache.LastBlockIndex;
            await this._feedsStorageService.UpdateFeedBlockIndexAsync(feedId, currentBlock);

            Console.WriteLine($"[UpdateGroupFeedSettings] Success - Title: {newTitle ?? "(unchanged)"}, Description: {(newDescription != null ? "updated" : "(unchanged)")}, IsPublic: {(isPublic.HasValue ? isPublic.Value.ToString() : "(unchanged)")}");
            return new UpdateGroupFeedSettingsResponse
            {
                Success = true,
                Message = "Group settings updated successfully"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UpdateGroupFeedSettings] ERROR: {ex.Message}");
            return new UpdateGroupFeedSettingsResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<DeleteGroupFeedResponse> DeleteGroupFeed(
        DeleteGroupFeedRequest request,
        ServerCallContext context)
    {
        Console.WriteLine($"[DeleteGroupFeed] FeedId: {request.FeedId}, Admin: {request.AdminPublicAddress?.Substring(0, Math.Min(10, request.AdminPublicAddress?.Length ?? 0))}...");

        try
        {
            var feedId = FeedIdHandler.CreateFromString(request.FeedId);
            var adminAddress = request.AdminPublicAddress ?? string.Empty;

            // Step 1: Check if group exists and is not already deleted
            var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
            if (groupFeed == null)
            {
                return new DeleteGroupFeedResponse
                {
                    Success = false,
                    Message = "Group not found"
                };
            }

            if (groupFeed.IsDeleted)
            {
                return new DeleteGroupFeedResponse
                {
                    Success = false,
                    Message = "Group has already been deleted"
                };
            }

            // Step 2: Validate requester is an admin (any admin can delete)
            var isAdmin = await this._feedsStorageService.IsAdminAsync(feedId, adminAddress);
            if (!isAdmin)
            {
                return new DeleteGroupFeedResponse
                {
                    Success = false,
                    Message = "Only administrators can delete the group"
                };
            }

            // Step 3: Delete the group (soft delete - marks as deleted)
            await this._feedsStorageService.MarkGroupFeedDeletedAsync(feedId);

            // Step 4: Update feed BlockIndex to notify clients
            var currentBlock = this._blockchainCache.LastBlockIndex;
            await this._feedsStorageService.UpdateFeedBlockIndexAsync(feedId, currentBlock);

            Console.WriteLine($"[DeleteGroupFeed] Success");
            return new DeleteGroupFeedResponse
            {
                Success = true,
                Message = "Group deleted successfully"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DeleteGroupFeed] ERROR: {ex.Message}");
            return new DeleteGroupFeedResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    // ===== Search Public Groups =====

    public override async Task<SearchPublicGroupsResponse> SearchPublicGroups(
        SearchPublicGroupsRequest request,
        ServerCallContext context)
    {
        Console.WriteLine($"[SearchPublicGroups] Query: '{request.SearchQuery}', MaxResults: {request.MaxResults}");

        try
        {
            var maxResults = request.MaxResults > 0 ? request.MaxResults : 20;
            var groups = await this._feedsStorageService.SearchPublicGroupsAsync(
                request.SearchQuery ?? string.Empty,
                maxResults);

            var response = new SearchPublicGroupsResponse
            {
                Success = true,
                Message = $"Found {groups.Count} public groups"
            };

            foreach (var group in groups)
            {
                // Count active members for each group
                var activeParticipants = await this._feedsStorageService.GetActiveParticipantsAsync(group.FeedId);

                response.Groups.Add(new PublicGroupInfoProto
                {
                    FeedId = group.FeedId.ToString(),
                    Title = group.Title,
                    Description = group.Description ?? string.Empty,
                    MemberCount = activeParticipants.Count,
                    CreatedAtBlock = group.CreatedAtBlock.Value
                });
            }

            Console.WriteLine($"[SearchPublicGroups] Returning {response.Groups.Count} groups");
            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SearchPublicGroups] ERROR: {ex.Message}");
            return new SearchPublicGroupsResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    // ===== FEAT-052: GetMessageById Endpoint =====

    public override async Task<GetMessageByIdResponse> GetMessageById(
        GetMessageByIdRequest request,
        ServerCallContext context)
    {
        Console.WriteLine($"[GetMessageById] FeedId: {request.FeedId}, MessageId: {request.MessageId}");

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
                Console.WriteLine($"[GetMessageById] Message not found: {request.MessageId}");
                return new GetMessageByIdResponse
                {
                    Success = false,
                    Error = "Message not found"
                };
            }

            // Verify the message belongs to the requested feed
            if (feedMessage.FeedId != feedId)
            {
                Console.WriteLine($"[GetMessageById] Message {request.MessageId} does not belong to feed {request.FeedId}");
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

            Console.WriteLine($"[GetMessageById] Success - returning message from feed {feedMessage.FeedId}");
            return new GetMessageByIdResponse
            {
                Success = true,
                Message = messageProto
            };
        }
        catch (FormatException ex)
        {
            Console.WriteLine($"[GetMessageById] Invalid ID format: {ex.Message}");
            return new GetMessageByIdResponse
            {
                Success = false,
                Error = "Invalid message ID format"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GetMessageById] ERROR: {ex.GetType().Name}: {ex.Message}");
            return new GetMessageByIdResponse
            {
                Success = false,
                Error = $"Internal error: {ex.Message}"
            };
        }
    }
}
