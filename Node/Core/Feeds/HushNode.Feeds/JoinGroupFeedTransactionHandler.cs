using HushNode.Caching;
using HushNode.Events;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using Olimpo;

namespace HushNode.Feeds;

/// <summary>
/// Transaction handler that processes validated JoinGroupFeed transactions.
/// Adds the joining user as a participant to the group and triggers key rotation
/// to distribute encryption keys to the new member.
/// </summary>
public class JoinGroupFeedTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache,
    IKeyRotationService keyRotationService,
    IUserFeedsCacheService userFeedsCacheService,
    IFeedParticipantsCacheService feedParticipantsCacheService,
    IGroupMembersCacheService groupMembersCacheService,
    IFeedMetadataCacheService feedMetadataCacheService,
    IEventAggregator eventAggregator)
    : IJoinGroupFeedTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IKeyRotationService _keyRotationService = keyRotationService;
    private readonly IUserFeedsCacheService _userFeedsCacheService = userFeedsCacheService;
    private readonly IFeedParticipantsCacheService _feedParticipantsCacheService = feedParticipantsCacheService;
    private readonly IGroupMembersCacheService _groupMembersCacheService = groupMembersCacheService;
    private readonly IFeedMetadataCacheService _feedMetadataCacheService = feedMetadataCacheService;
    private readonly IEventAggregator _eventAggregator = eventAggregator;

    public async Task HandleJoinGroupFeedTransactionAsync(ValidatedTransaction<JoinGroupFeedPayload> transaction)
    {
        var payload = transaction.Payload;
        var currentBlock = this._blockchainCache.LastBlockIndex;
        var joiningUserAddress = payload.JoiningUserPublicAddress;

        // Check if this is a rejoin (user was previously a member)
        var existingParticipant = await this._feedsStorageService
            .GetParticipantWithHistoryAsync(payload.FeedId, joiningUserAddress);

        if (existingParticipant != null && existingParticipant.LeftAtBlock != null)
        {
            // Rejoin: Update existing participant
            await this._feedsStorageService.UpdateParticipantRejoinAsync(
                payload.FeedId,
                joiningUserAddress,
                currentBlock,
                ParticipantType.Member);
        }
        else if (existingParticipant == null)
        {
            // New join: Create new participant entity
            var newParticipant = new GroupFeedParticipantEntity(
                payload.FeedId,
                joiningUserAddress,
                ParticipantType.Member,
                currentBlock,
                LeftAtBlock: null,
                LastLeaveBlock: null);

            await this._feedsStorageService.AddParticipantAsync(payload.FeedId, newParticipant);
        }
        // Note: If existingParticipant != null && LeftAtBlock == null, they're already a member
        // This case should have been caught by validation, but we don't throw - just no-op

        // Trigger key rotation to distribute encryption keys to the new member
        // This ensures the joining user immediately receives keys to send and receive messages
        var keyRotationResult = await this._keyRotationService.TriggerAndPersistRotationAsync(
            payload.FeedId,
            RotationTrigger.Join,
            joiningMemberAddress: joiningUserAddress,
            leavingMemberAddress: null);

        if (!keyRotationResult.IsSuccess)
        {
            Console.WriteLine($"[JoinGroupFeed] WARNING: Key rotation failed for feed {payload.FeedId.Value.ToString().Substring(0, 8)}...");
            Console.WriteLine($"[JoinGroupFeed] Reason: {keyRotationResult.ErrorMessage}");
            Console.WriteLine($"[JoinGroupFeed] User {joiningUserAddress.Substring(0, 10)}... joined but cannot decrypt messages.");
        }
        else
        {
            Console.WriteLine($"[JoinGroupFeed] Key rotation succeeded for feed {payload.FeedId.Value.ToString().Substring(0, 8)}... - KeyGen: {keyRotationResult.NewKeyGeneration}");

            // CRITICAL: Invalidate KeyGenerations cache SYNCHRONOUSLY before client queries
            // The async event (UserJoinedGroupEvent) causes a race condition where the client
            // queries the cache before invalidation happens
            await this._feedParticipantsCacheService.InvalidateKeyGenerationsAsync(payload.FeedId);
            Console.WriteLine($"[JoinGroupFeed] KeyGenerations cache invalidated for feed {payload.FeedId.Value.ToString().Substring(0, 8)}...");

            // CRITICAL: Update LastUpdatedAtBlock so existing members receive new keys in their delta sync
            // Without this, existing members' delta sync won't return the group (JoinedAtBlock < blockIndex)
            // and they won't receive the new KeyGeneration needed to decrypt messages from new members
            await this._feedsStorageService.UpdateGroupFeedLastUpdatedAtBlockAsync(payload.FeedId, currentBlock);
            Console.WriteLine($"[JoinGroupFeed] LastUpdatedAtBlock set to {currentBlock} for feed {payload.FeedId.Value.ToString().Substring(0, 8)}...");
        }

        // CRITICAL: Add participant to cache SYNCHRONOUSLY before client queries
        // The async event publishing causes a race condition where the client
        // queries GetFeedsForAddress before the cache is updated
        await this._feedParticipantsCacheService.AddParticipantAsync(payload.FeedId, joiningUserAddress);
        Console.WriteLine($"[JoinGroupFeed] Participant added to cache for feed {payload.FeedId.Value.ToString().Substring(0, 8)}...");

        // CRITICAL: Invalidate group members cache so GetGroupMembers returns fresh data including the new member
        // Without this, clients see "You are no longer a member" because the cached member list doesn't include them
        await this._groupMembersCacheService.InvalidateGroupMembersAsync(payload.FeedId);
        Console.WriteLine($"[JoinGroupFeed] Group members cache invalidated for feed {payload.FeedId.Value.ToString().Substring(0, 8)}...");

        // Update the user's feed list cache (FEAT-049)
        // Cache update is fire-and-forget - failure does not affect the transaction
        await this._userFeedsCacheService.AddFeedToUserCacheAsync(joiningUserAddress, payload.FeedId);

        // FEAT-065: Populate feed_meta with full metadata for the joining member
        try
        {
            var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(payload.FeedId);
            var participants = await this._feedParticipantsCacheService.GetParticipantsAsync(payload.FeedId);
            var participantList = participants?.ToList() ?? new List<string> { joiningUserAddress };

            var entry = new FeedMetadataEntry
            {
                Title = groupFeed?.Title ?? string.Empty,
                Type = (int)FeedType.Group,
                LastBlockIndex = currentBlock.Value,
                Participants = participantList,
                CreatedAtBlock = groupFeed?.CreatedAtBlock.Value ?? currentBlock.Value,
                CurrentKeyGeneration = groupFeed?.CurrentKeyGeneration
            };
            _ = this._feedMetadataCacheService.SetFeedMetadataAsync(
                joiningUserAddress, payload.FeedId, entry);
        }
        catch (Exception)
        {
            // Fire-and-forget: cache failure should not block join transaction
        }

        // Publish event for other listeners (e.g., notifications)
        // Note: Cache updates are done synchronously above to avoid race conditions
        _ = this._eventAggregator.PublishAsync(new UserJoinedGroupEvent(
            payload.FeedId,
            joiningUserAddress,
            currentBlock));
    }
}
