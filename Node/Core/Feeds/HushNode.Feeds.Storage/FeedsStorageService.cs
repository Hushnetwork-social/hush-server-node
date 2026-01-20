using HushNode.Caching;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
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

    public async Task<bool> CreatePersonalFeedIfNotExists(Feed feed, string publicSigningAddress)
    {
        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Use serializable isolation to prevent race conditions where two transactions
                // both check HasPersonalFeed=false and then both create a feed
                using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable(
                    System.Data.IsolationLevel.Serializable);
                var repository = writableUnitOfWork.GetRepository<IFeedsRepository>();

                // Check within the same serializable transaction
                var hasPersonalFeed = await repository.HasPersonalFeed(publicSigningAddress);
                if (hasPersonalFeed)
                {
                    return false; // Personal feed already exists
                }

                await repository.CreateFeed(feed);
                await writableUnitOfWork.CommitAsync();
                return true;
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "40001") // Serialization failure
            {
                if (attempt == maxRetries)
                    throw; // Rethrow on final attempt

                // Wait a bit before retrying (exponential backoff)
                await Task.Delay(50 * attempt);
            }
        }

        return false; // Should not reach here
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
