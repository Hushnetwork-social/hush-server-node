using HushNode.Caching;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Feeds.Storage;

public class FeedsStorageService(
    IUnitOfWorkProvider<FeedsDbContext> unitOfWorkProvider,
    IUserFeedsCacheService userFeedsCacheService,
    ILogger<FeedsStorageService> logger)
    : IFeedsStorageService
{
    private readonly IUnitOfWorkProvider<FeedsDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly IUserFeedsCacheService _userFeedsCacheService = userFeedsCacheService;
    private readonly ILogger<FeedsStorageService> _logger = logger;

    public async Task CreateGroupFeed(GroupFeed groupFeed)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .CreateGroupFeed(groupFeed);

        await writableUnitOfWork.CommitAsync();
    }

    public async Task<bool> HasPersonalFeed(string publicSigningAddress) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .HasPersonalFeed(publicSigningAddress);

    public async Task<bool> IsFeedIsBlockchain(FeedId feedId) => 
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .IsFeedInBlockchain(feedId);

    public async Task CreateFeed(Feed feed)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .CreateFeed(feed);

        await writableUnitOfWork.CommitAsync();
    }

    /// <summary>
    /// Creates a personal feed if one doesn't already exist for the user.
    /// Uses PostgreSQL advisory locks to prevent race conditions without blocking readers.
    ///
    /// Why advisory locks instead of SERIALIZABLE isolation?
    /// - SERIALIZABLE transactions acquire "predicate locks" that conflict with ANY reader
    /// - Client sync continuously reads from the Feeds table (every 3 seconds)
    /// - This caused frequent 40001 serialization failures in E2E tests
    /// - Advisory locks only block OTHER writers for the SAME address, not readers
    /// </summary>
    public async Task<bool> CreatePersonalFeedIfNotExists(Feed feed, string publicSigningAddress)
    {
        // Step 1: Quick check OUTSIDE any transaction (READ COMMITTED, no locks held)
        // This handles the common case where the feed already exists without any locking
        var alreadyExists = await this.HasPersonalFeed(publicSigningAddress);
        if (alreadyExists)
        {
            this._logger.LogDebug(
                "Personal feed already exists for {Address} (fast path)",
                publicSigningAddress);
            return false;
        }

        // Step 2: Acquire advisory lock and create the feed
        // Advisory locks block OTHER writers for the same address but NOT readers
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        // Hash the address to get a unique lock key for PostgreSQL advisory lock
        var lockKey = GetAdvisoryLockKey(publicSigningAddress);

        // Acquire transaction-scoped advisory lock (auto-released on commit/rollback)
        // This will wait if another transaction holds the same lock
        await writableUnitOfWork.Context.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0})", lockKey);

        this._logger.LogDebug(
            "Acquired advisory lock {LockKey} for personal feed creation",
            lockKey);

        var repository = writableUnitOfWork.GetRepository<IFeedsRepository>();

        // Re-check after acquiring lock (another transaction might have just created it)
        var hasPersonalFeed = await repository.HasPersonalFeed(publicSigningAddress);
        if (hasPersonalFeed)
        {
            this._logger.LogDebug(
                "Personal feed was created by concurrent transaction for {Address}",
                publicSigningAddress);
            return false;
        }

        await repository.CreateFeed(feed);
        await writableUnitOfWork.CommitAsync();

        this._logger.LogInformation(
            "Created personal feed {FeedId} for {Address}",
            feed.FeedId,
            publicSigningAddress);

        return true;
    }

    /// <summary>
    /// Generates a stable advisory lock key for a given public signing address.
    /// Uses a namespace prefix to avoid collisions with other advisory locks in the system.
    /// </summary>
    private static long GetAdvisoryLockKey(string publicSigningAddress)
    {
        // Namespace constant to avoid collisions with other advisory lock usages
        // "PF" = Personal Feed = 0x5046 in ASCII
        const long PERSONAL_FEED_NAMESPACE = 0x5046_0000_0000_0000L;

        // Use string hash code (stable within a single runtime)
        // Combined with namespace to create unique lock key
        return PERSONAL_FEED_NAMESPACE | (uint)publicSigningAddress.GetHashCode();
    }

    public async Task<IEnumerable<Feed>> RetrieveFeedsForAddress(string publicSigningAddress, BlockIndex blockIndex)
    {
        using var unitOfWork = this._unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IFeedsRepository>();

        // Get Personal/Chat feeds from Feeds table
        var feeds = await repository.RetrieveFeedsForAddress(publicSigningAddress, blockIndex);

        // Get Group feeds from GroupFeeds table (separate participant tracking)
        var groupFeeds = await repository.RetrieveGroupFeedsForAddress(publicSigningAddress, blockIndex);

        // Get Group feeds where the user has left (for viewing message history)
        // These are always returned regardless of blockIndex since they're historical
        var leftGroupFeeds = await repository.RetrieveLeftGroupFeedsForAddress(publicSigningAddress);

        // Combine active and left group feeds, avoiding duplicates
        var activeGroupFeedIds = groupFeeds.Select(g => g.FeedId).ToHashSet();
        var uniqueLeftGroupFeeds = leftGroupFeeds.Where(g => !activeGroupFeedIds.Contains(g.FeedId));

        // Convert GroupFeed to Feed for consistent API return type
        var groupFeedsAsFeed = groupFeeds.Select(g => ConvertGroupFeedToFeed(g, publicSigningAddress));
        var leftGroupFeedsAsFeed = uniqueLeftGroupFeeds.Select(g => ConvertGroupFeedToFeed(g, publicSigningAddress));

        return feeds.Concat(groupFeedsAsFeed).Concat(leftGroupFeedsAsFeed);
    }

    /// <summary>
    /// Converts a GroupFeed to a Feed for consistent API response.
    /// The Feed object is used by gRPC service for all feed types.
    /// </summary>
    private static Feed ConvertGroupFeedToFeed(GroupFeed groupFeed, string requesterPublicAddress)
    {
        var feed = new Feed(groupFeed.FeedId, groupFeed.Title, FeedType.Group, groupFeed.CreatedAtBlock);

        // Find the current KeyGeneration and the user's encrypted key
        var currentKeyGen = groupFeed.KeyGenerations
            .FirstOrDefault(kg => kg.KeyGeneration == groupFeed.CurrentKeyGeneration);

        var userEncryptedKey = currentKeyGen?.EncryptedKeys
            .FirstOrDefault(ek => ek.MemberPublicAddress == requesterPublicAddress)
            ?.EncryptedAesKey ?? string.Empty;

        // Convert GroupFeedParticipantEntity to FeedParticipant
        foreach (var participant in groupFeed.Participants.Where(p => p.LeftAtBlock == null))
        {
            // For the requesting user, include their encrypted feed key from the current KeyGeneration
            var encryptedFeedKey = participant.ParticipantPublicAddress == requesterPublicAddress
                ? userEncryptedKey
                : string.Empty;

            feed.Participants = feed.Participants.Append(new FeedParticipant(
                groupFeed.FeedId,
                participant.ParticipantPublicAddress,
                participant.ParticipantType,
                encryptedFeedKey)
            {
                Feed = feed
            }).ToArray();
        }

        return feed;
    }

    public async Task<Feed?> GetFeedByIdAsync(FeedId feedId) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .GetFeedByIdAsync(feedId);

    /// <summary>
    /// Gets all feed IDs for a user with cache-aside pattern (FEAT-049).
    /// Tries Redis cache first, falls back to PostgreSQL on miss.
    /// </summary>
    public async Task<IReadOnlyList<FeedId>> GetFeedIdsForUserAsync(string publicAddress)
    {
        try
        {
            // 1. Try cache first
            var cachedFeedIds = await this._userFeedsCacheService.GetUserFeedsAsync(publicAddress);

            if (cachedFeedIds != null)
            {
                // Cache hit - return cached data
                _logger.LogDebug(
                    "Cache HIT for user {UserAddress}: returning {FeedCount} cached feed IDs",
                    publicAddress,
                    cachedFeedIds.Count);

                return cachedFeedIds;
            }

            // 2. Cache miss - query PostgreSQL
            _logger.LogDebug(
                "Cache MISS for user {UserAddress}: querying PostgreSQL",
                publicAddress);

            var dbFeedIds = await this._unitOfWorkProvider
                .CreateReadOnly()
                .GetRepository<IFeedsRepository>()
                .GetFeedIdsForUserAsync(publicAddress);

            // 3. Cache-aside: Populate cache with fetched feed IDs (only if non-empty)
            if (dbFeedIds.Count > 0)
            {
                try
                {
                    await this._userFeedsCacheService.SetUserFeedsAsync(publicAddress, dbFeedIds);
                    _logger.LogDebug(
                        "Populated cache for user {UserAddress} with {FeedCount} feed IDs",
                        publicAddress,
                        dbFeedIds.Count);
                }
                catch (Exception ex)
                {
                    // Log and continue - cache population failure should not fail the request
                    _logger.LogWarning(
                        ex,
                        "Failed to populate cache for user {UserAddress}. Returning PostgreSQL results.",
                        publicAddress);
                }
            }

            return dbFeedIds;
        }
        catch (Exception ex)
        {
            // Redis error - fall back to PostgreSQL
            _logger.LogWarning(
                ex,
                "Cache operation failed for user {UserAddress}. Falling back to PostgreSQL.",
                publicAddress);

            return await this._unitOfWorkProvider
                .CreateReadOnly()
                .GetRepository<IFeedsRepository>()
                .GetFeedIdsForUserAsync(publicAddress);
        }
    }

    public async Task UpdateFeedsBlockIndexForParticipantAsync(string publicSigningAddress, BlockIndex blockIndex)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .UpdateFeedsBlockIndexForParticipantAsync(publicSigningAddress, blockIndex);

        await writableUnitOfWork.CommitAsync();
    }

    public async Task<IReadOnlyList<FeedId>> GetGroupFeedIdsForUserAsync(string publicAddress) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .GetGroupFeedIdsForUserAsync(publicAddress);

    public async Task UpdateGroupFeedsLastUpdatedAtBlockForParticipantAsync(string publicSigningAddress, BlockIndex blockIndex)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .UpdateGroupFeedsLastUpdatedAtBlockForParticipantAsync(publicSigningAddress, blockIndex);

        await writableUnitOfWork.CommitAsync();
    }

    public async Task UpdateGroupFeedLastUpdatedAtBlockAsync(FeedId feedId, BlockIndex blockIndex)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .UpdateGroupFeedLastUpdatedAtBlockAsync(feedId, blockIndex);

        await writableUnitOfWork.CommitAsync();
    }

    // ===== Group Feed Admin Operations (FEAT-009) =====

    public async Task<GroupFeed?> GetGroupFeedAsync(FeedId feedId) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .GetGroupFeedAsync(feedId);

    public async Task<GroupFeedParticipantEntity?> GetGroupFeedParticipantAsync(FeedId feedId, string publicAddress) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .GetGroupFeedParticipantAsync(feedId, publicAddress);

    public async Task UpdateParticipantTypeAsync(FeedId feedId, string publicAddress, ParticipantType newType)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .UpdateParticipantTypeAsync(feedId, publicAddress, newType);

        await writableUnitOfWork.CommitAsync();
    }

    public async Task<bool> IsAdminAsync(FeedId feedId, string publicAddress) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .IsAdminAsync(feedId, publicAddress);

    public async Task<int> GetAdminCountAsync(FeedId feedId) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .GetAdminCountAsync(feedId);

    // ===== Group Feed Metadata Operations (FEAT-009 Phase 4) =====

    public async Task UpdateGroupFeedTitleAsync(FeedId feedId, string newTitle)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .UpdateGroupFeedTitleAsync(feedId, newTitle);

        await writableUnitOfWork.CommitAsync();
    }

    public async Task UpdateGroupFeedDescriptionAsync(FeedId feedId, string newDescription)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .UpdateGroupFeedDescriptionAsync(feedId, newDescription);

        await writableUnitOfWork.CommitAsync();
    }

    public async Task UpdateGroupFeedSettingsAsync(FeedId feedId, string? newTitle, string? newDescription, bool? isPublic)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .UpdateGroupFeedSettingsAsync(feedId, newTitle, newDescription, isPublic);

        await writableUnitOfWork.CommitAsync();
    }

    public async Task MarkGroupFeedDeletedAsync(FeedId feedId)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .MarkGroupFeedDeletedAsync(feedId);

        await writableUnitOfWork.CommitAsync();
    }

    // ===== Group Feed Join/Leave Operations (FEAT-008) =====

    public async Task AddParticipantAsync(FeedId feedId, GroupFeedParticipantEntity participant)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .AddParticipantAsync(feedId, participant);

        await writableUnitOfWork.CommitAsync();
    }

    public async Task UpdateParticipantLeaveStatusAsync(FeedId feedId, string publicAddress, BlockIndex leftAtBlock)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .UpdateParticipantLeaveStatusAsync(feedId, publicAddress, leftAtBlock);

        await writableUnitOfWork.CommitAsync();
    }

    public async Task UpdateParticipantRejoinAsync(FeedId feedId, string publicAddress, BlockIndex joinedAtBlock, ParticipantType participantType)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .UpdateParticipantRejoinAsync(feedId, publicAddress, joinedAtBlock, participantType);

        await writableUnitOfWork.CommitAsync();
    }

    public async Task UpdateParticipantBanAsync(FeedId feedId, string publicAddress, BlockIndex bannedAtBlock)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .UpdateParticipantBanAsync(feedId, publicAddress, bannedAtBlock);

        await writableUnitOfWork.CommitAsync();
    }

    public async Task UpdateParticipantUnbanAsync(FeedId feedId, string publicAddress, BlockIndex rejoinedAtBlock)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .UpdateParticipantUnbanAsync(feedId, publicAddress, rejoinedAtBlock);

        await writableUnitOfWork.CommitAsync();
    }

    public async Task<bool> IsBannedAsync(FeedId feedId, string publicAddress) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .IsBannedAsync(feedId, publicAddress);

    public async Task<GroupFeedParticipantEntity?> GetParticipantWithHistoryAsync(FeedId feedId, string publicAddress) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .GetParticipantWithHistoryAsync(feedId, publicAddress);

    public async Task<IReadOnlyList<GroupFeedParticipantEntity>> GetActiveParticipantsAsync(FeedId feedId) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .GetActiveParticipantsAsync(feedId);

    public async Task<IReadOnlyList<GroupFeedParticipantEntity>> GetAllParticipantsAsync(FeedId feedId) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .GetAllParticipantsAsync(feedId);

    public async Task AddKeyGenerationAsync(FeedId feedId, GroupFeedKeyGenerationEntity keyGeneration)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .AddKeyGenerationAsync(feedId, keyGeneration);

        await writableUnitOfWork.CommitAsync();
    }

    // ===== Key Rotation Operations (FEAT-010) =====

    public async Task<int?> GetMaxKeyGenerationAsync(FeedId feedId) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .GetMaxKeyGenerationAsync(feedId);

    public async Task<IReadOnlyList<string>> GetActiveGroupMemberAddressesAsync(FeedId feedId) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .GetActiveGroupMemberAddressesAsync(feedId);

    public async Task CreateKeyRotationAsync(GroupFeedKeyGenerationEntity keyGeneration)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();
        var repository = writableUnitOfWork.GetRepository<IFeedsRepository>();

        // Create the key generation entity with encrypted keys
        await repository.CreateKeyRotationAsync(keyGeneration);

        // Update the group's CurrentKeyGeneration
        await repository.UpdateCurrentKeyGenerationAsync(keyGeneration.FeedId, keyGeneration.KeyGeneration);

        // Commit atomically
        await writableUnitOfWork.CommitAsync();
    }

    // ===== Group Messaging Operations (FEAT-011) =====

    public async Task<GroupFeedKeyGenerationEntity?> GetKeyGenerationByNumberAsync(FeedId feedId, int keyGeneration) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .GetKeyGenerationByNumberAsync(feedId, keyGeneration);

    public async Task<bool> CanMemberSendMessagesAsync(FeedId feedId, string publicAddress) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .CanMemberSendMessagesAsync(feedId, publicAddress);

    // ===== Group Feed Query Operations (FEAT-017) =====

    public async Task<IReadOnlyList<GroupFeedKeyGenerationEntity>> GetKeyGenerationsForUserAsync(FeedId feedId, string publicAddress) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .GetKeyGenerationsForUserAsync(feedId, publicAddress);

    public async Task<IReadOnlyList<GroupFeedKeyGenerationEntity>> GetAllKeyGenerationsAsync(FeedId feedId) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .GetAllKeyGenerationsAsync(feedId);

    public async Task UpdateFeedBlockIndexAsync(FeedId feedId, BlockIndex blockIndex)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .UpdateFeedBlockIndexAsync(feedId, blockIndex);

        await writableUnitOfWork.CommitAsync();
    }

    // ===== Public Group Search Operations =====

    public async Task<IReadOnlyList<GroupFeed>> SearchPublicGroupsAsync(string searchQuery, int maxResults = 20) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .SearchPublicGroupsAsync(searchQuery, maxResults);

    // ===== Invite Code Operations =====

    public async Task<GroupFeed?> GetGroupFeedByInviteCodeAsync(string inviteCode) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .GetGroupFeedByInviteCodeAsync(inviteCode);

    public async Task<string> GenerateInviteCodeAsync(FeedId feedId)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        var code = await writableUnitOfWork
            .GetRepository<IFeedsRepository>()
            .GenerateInviteCodeAsync(feedId);

        await writableUnitOfWork.CommitAsync();

        return code;
    }
}
