using Grpc.Core;
using HushNetwork.proto;
using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushNode.Identity.Storage;
using HushNode.Reactions.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using MessageReactionTallyModel = HushShared.Reactions.Model.MessageReactionTally;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Feeds.gRPC;

public partial class FeedsGrpcService(
    IFeedsStorageService feedsStorageService,
    IFeedMessageStorageService feedMessageStorageService,
    IFeedMessageCacheService feedMessageCacheService,
    IFeedParticipantsCacheService feedParticipantsCacheService,
    IGroupMembersCacheService groupMembersCacheService,
    IFeedReadPositionStorageService feedReadPositionStorageService,
    IFeedMetadataCacheService feedMetadataCacheService,
    IIdentityDisplayNameCacheService identityDisplayNameCacheService,
    IInnerCircleApplicationService innerCircleApplicationService,
    ISocialComposerApplicationService socialComposerApplicationService,
    ISocialPostApplicationService socialPostApplicationService,
    IGroupMembershipApplicationService groupMembershipApplicationService,
    IGroupAdministrationApplicationService groupAdministrationApplicationService,
    IIdentityService identityService,
    IBlockchainCache blockchainCache,
    IUnitOfWorkProvider<ReactionsDbContext> reactionsUnitOfWorkProvider,
    IAttachmentStorageService attachmentStorageService,
    IConfiguration configuration,
    ILogger<FeedsGrpcService> logger) : HushFeed.HushFeedBase
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IFeedMessageStorageService _feedMessageStorageService = feedMessageStorageService;
    private readonly IFeedMessageCacheService _feedMessageCacheService = feedMessageCacheService;
    private readonly IFeedParticipantsCacheService _feedParticipantsCacheService = feedParticipantsCacheService;
    private readonly IGroupMembersCacheService _groupMembersCacheService = groupMembersCacheService;
    private readonly IFeedReadPositionStorageService _feedReadPositionStorageService = feedReadPositionStorageService;
    private readonly IFeedMetadataCacheService _feedMetadataCacheService = feedMetadataCacheService;
    private readonly IIdentityDisplayNameCacheService _identityDisplayNameCacheService = identityDisplayNameCacheService;
    private readonly IInnerCircleApplicationService _innerCircleApplicationService = innerCircleApplicationService;
    private readonly ISocialComposerApplicationService _socialComposerApplicationService = socialComposerApplicationService;
    private readonly ISocialPostApplicationService _socialPostApplicationService = socialPostApplicationService;
    private readonly IGroupMembershipApplicationService _groupMembershipApplicationService = groupMembershipApplicationService;
    private readonly IGroupAdministrationApplicationService _groupAdministrationApplicationService = groupAdministrationApplicationService;
    private readonly IIdentityService _identityService = identityService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IUnitOfWorkProvider<ReactionsDbContext> _reactionsUnitOfWorkProvider = reactionsUnitOfWorkProvider;
    private readonly IAttachmentStorageService _attachmentStorageService = attachmentStorageService;
    private readonly int _maxMessagesPerResponse = configuration.GetValue<int>("Feeds:MaxMessagesPerResponse", 100);
    private readonly ILogger<FeedsGrpcService> _logger = logger;

    /// <summary>FEAT-066: Chunk size for streaming attachment downloads (~64KB).</summary>
    private const int AttachmentChunkSize = 65536;

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

        // PostgreSQL feed query still needed for participant encrypted keys
        var lastFeeds = await this._feedsStorageService
            .RetrieveFeedsForAddress(request.ProfilePublicKey, blockIndex);

        _logger.LogInformation(
            "[GetFeedsForAddress] Found {FeedCount} feed(s) for address",
            lastFeeds.Count());

        // FEAT-065: Try full metadata cache (titles, lastBlockIndex, type, etc.)
        IReadOnlyDictionary<FeedId, FeedMetadataEntry>? cachedMetadata = null;
        try
        {
            cachedMetadata = await _feedMetadataCacheService.GetAllFeedMetadataAsync(request.ProfilePublicKey);

            // Cache miss: populate from PostgreSQL feed data with resolved titles
            if (cachedMetadata == null && lastFeeds.Any())
            {
                cachedMetadata = await PopulateFeedMetadataCacheAsync(request.ProfilePublicKey, lastFeeds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch feed metadata from Redis for user {UserId}, resolving titles from PostgreSQL", request.ProfilePublicKey);
        }

        // FEAT-051: Batch fetch read positions for all feeds (cache-aside pattern with DB fallback)
        IReadOnlyDictionary<FeedId, BlockIndex> readPositions;
        try
        {
            readPositions = await _feedReadPositionStorageService.GetReadPositionsForUserAsync(request.ProfilePublicKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch read positions for user {UserId}, defaulting to 0", request.ProfilePublicKey);
            readPositions = new Dictionary<FeedId, BlockIndex>();
        }

        var reply = new GetFeedForAddressReply();
        foreach(var feed in lastFeeds)
        {
            // FEAT-065: Use cached title if available, fall back to PostgreSQL identity lookup
            string feedAlias;
            long effectiveBlockIndex;

            if (cachedMetadata != null && cachedMetadata.TryGetValue(feed.FeedId, out var cachedEntry))
            {
                // Cache hit: use cached title (no identity lookups needed)
                feedAlias = cachedEntry.Title ?? string.Empty;
                // Use MAX of cached lastBlockIndex and PostgreSQL BlockIndex
                effectiveBlockIndex = Math.Max(cachedEntry.LastBlockIndex, feed.BlockIndex.Value);
            }
            else
            {
                // Cache miss for this feed: resolve title from PostgreSQL (existing logic)
                feedAlias = feed.FeedType switch
                {
                    FeedType.Personal => await ExtractPersonalFeedAlias(feed),
                    FeedType.Chat => await ExtractChatFeedAlias(feed, request.ProfilePublicKey),
                    FeedType.Group => await ExtractGroupFeedAlias(feed),
                    FeedType.Broadcast => await ExtractBroascastAlias(feed),
                    _ => throw new InvalidOperationException($"the FeedTYype {feed.FeedType} is not supported.")
                };
                effectiveBlockIndex = await GetEffectiveBlockIndex(feed);
            }

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

    /// <summary>
    /// FEAT-065: Populate full feed metadata cache from PostgreSQL on cache miss.
    /// Resolves titles for all feed types and writes to Redis via HMSET.
    /// Returns the populated metadata for immediate use.
    /// </summary>
    private async Task<IReadOnlyDictionary<FeedId, FeedMetadataEntry>> PopulateFeedMetadataCacheAsync(
        string userId, IEnumerable<Feed> feeds)
    {
        var entries = new Dictionary<FeedId, FeedMetadataEntry>();

        foreach (var feed in feeds)
        {
            var title = feed.FeedType switch
            {
                FeedType.Personal => await ExtractPersonalFeedAlias(feed),
                FeedType.Chat => await ExtractChatFeedAlias(feed, userId),
                FeedType.Group => await ExtractGroupFeedAlias(feed),
                _ => feed.Alias
            };

            var participantAddresses = feed.Participants
                .Select(p => p.ParticipantPublicAddress)
                .ToList();

            // Get group-specific metadata if applicable
            int? currentKeyGeneration = null;
            if (feed.FeedType == FeedType.Group)
            {
                var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feed.FeedId);
                currentKeyGeneration = groupFeed?.CurrentKeyGeneration;
            }

            entries[feed.FeedId] = new FeedMetadataEntry
            {
                Title = title,
                Type = (int)feed.FeedType,
                LastBlockIndex = feed.BlockIndex.Value,
                Participants = participantAddresses,
                CreatedAtBlock = feed.BlockIndex.Value,
                CurrentKeyGeneration = currentKeyGeneration
            };
        }

        // Write to Redis atomically (fire-and-forget for the write, but return the data)
        _ = _feedMetadataCacheService.SetMultipleFeedMetadataAsync(userId, entries);

        // Also populate identity display name cache for all participant addresses
        await PopulateIdentityDisplayNameCacheAsync(feeds);

        return entries;
    }

    /// <summary>
    /// FEAT-065 E2: Populate identity display name cache for all unique participant addresses.
    /// Called during feed metadata cache population to warm the identity cache.
    /// </summary>
    private async Task PopulateIdentityDisplayNameCacheAsync(IEnumerable<Feed> feeds)
    {
        try
        {
            var uniqueAddresses = feeds
                .SelectMany(f => f.Participants.Select(p => p.ParticipantPublicAddress))
                .Distinct()
                .ToList();

            if (uniqueAddresses.Count == 0)
                return;

            // Check which names are already cached
            var cachedNames = await _identityDisplayNameCacheService.GetDisplayNamesAsync(uniqueAddresses);
            if (cachedNames == null)
                return; // Redis failure, skip population

            // Find addresses with cache misses
            var missingAddresses = cachedNames
                .Where(kvp => kvp.Value == null)
                .Select(kvp => kvp.Key)
                .ToList();

            if (missingAddresses.Count == 0)
                return;

            // Resolve from identity service and populate cache
            var namesToCache = new Dictionary<string, string>();
            foreach (var address in missingAddresses)
            {
                var displayName = await ExtractDisplayName(address);
                namesToCache[address] = displayName;
            }

            if (namesToCache.Count > 0)
            {
                _ = _identityDisplayNameCacheService.SetMultipleDisplayNamesAsync(namesToCache);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to populate identity display name cache");
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

    /// <summary>
    /// FEAT-065: Computes effective block index for a feed by considering
    /// participant profile block indexes in addition to the feed block index.
    /// </summary>
    private async Task<long> GetEffectiveBlockIndex(Feed feed, BlockIndex? blockIndexOverride = null)
    {
        var maxBlockIndex = blockIndexOverride?.Value ?? feed.BlockIndex.Value;

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
}
