using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

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
    IUserFeedsCacheService userFeedsCacheService)
    : IJoinGroupFeedTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IKeyRotationService _keyRotationService = keyRotationService;
    private readonly IUserFeedsCacheService _userFeedsCacheService = userFeedsCacheService;

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
        await this._keyRotationService.TriggerAndPersistRotationAsync(
            payload.FeedId,
            RotationTrigger.Join,
            joiningMemberAddress: joiningUserAddress,
            leavingMemberAddress: null);

        // Update the user's feed list cache (FEAT-049)
        // Cache update is fire-and-forget - failure does not affect the transaction
        await this._userFeedsCacheService.AddFeedToUserCacheAsync(joiningUserAddress, payload.FeedId);
    }
}
