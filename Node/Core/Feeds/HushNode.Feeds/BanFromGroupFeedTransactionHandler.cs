using HushNode.Caching;
using HushNode.Events;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using Olimpo;

namespace HushNode.Feeds;

/// <summary>
/// Handler for BanFromGroupFeed transactions.
/// Sets participant status to Banned and triggers key rotation.
/// Unlike Block (FEAT-009), Ban is a cryptographic operation - banned member
/// will NOT receive new encryption keys and cannot decrypt future messages.
/// </summary>
public class BanFromGroupFeedTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache,
    IKeyRotationService keyRotationService,
    IUserFeedsCacheService userFeedsCacheService,
    IFeedMetadataCacheService feedMetadataCacheService,
    IEventAggregator eventAggregator)
    : IBanFromGroupFeedTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IKeyRotationService _keyRotationService = keyRotationService;
    private readonly IUserFeedsCacheService _userFeedsCacheService = userFeedsCacheService;
    private readonly IFeedMetadataCacheService _feedMetadataCacheService = feedMetadataCacheService;
    private readonly IEventAggregator _eventAggregator = eventAggregator;

    public async Task HandleBanFromGroupFeedTransactionAsync(ValidatedTransaction<BanFromGroupFeedPayload> banTransaction)
    {
        var payload = banTransaction.Payload;
        var currentBlock = this._blockchainCache.LastBlockIndex;

        // Step 1: Update participant status from Member/Blocked to Banned
        await this._feedsStorageService.UpdateParticipantTypeAsync(
            payload.FeedId,
            payload.BannedUserPublicAddress,
            ParticipantType.Banned);

        // Step 2: Trigger key rotation to exclude banned member from future key distribution
        // The leavingMemberAddress parameter ensures the banned member is excluded from the new key
        await this._keyRotationService.TriggerAndPersistRotationAsync(
            payload.FeedId,
            RotationTrigger.Ban,
            joiningMemberAddress: null,
            leavingMemberAddress: payload.BannedUserPublicAddress);

        // Step 3: Update the banned user's feed list cache (FEAT-049)
        // Cache update is fire-and-forget - failure does not affect the transaction
        await this._userFeedsCacheService.RemoveFeedFromUserCacheAsync(payload.BannedUserPublicAddress, payload.FeedId);

        // FEAT-065: Remove feed_meta entry for banned member
        _ = this._feedMetadataCacheService.RemoveFeedMetadataAsync(payload.BannedUserPublicAddress, payload.FeedId);

        // Step 4: Publish event for feed participants cache invalidation (FEAT-050)
        // Fire and forget - cache invalidation is secondary to blockchain state
        _ = this._eventAggregator.PublishAsync(new UserBannedFromGroupEvent(
            payload.FeedId,
            payload.BannedUserPublicAddress,
            currentBlock));
    }
}
