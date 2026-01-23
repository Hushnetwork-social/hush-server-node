using HushNode.Caching;
using HushNode.Feeds.Events;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using Olimpo;

namespace HushNode.Feeds;

public class NewGroupFeedTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache,
    IEventAggregator eventAggregator,
    IUserFeedsCacheService userFeedsCacheService)
    : INewGroupFeedTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IEventAggregator _eventAggregator = eventAggregator;
    private readonly IUserFeedsCacheService _userFeedsCacheService = userFeedsCacheService;

    public async Task HandleNewGroupFeedTransactionAsync(ValidatedTransaction<NewGroupFeedPayload> newGroupFeedTransaction)
    {
        var payload = newGroupFeedTransaction.Payload;
        var currentBlock = this._blockchainCache.LastBlockIndex;
        var creatorAddress = newGroupFeedTransaction.UserSignature?.Signatory
            ?? throw new InvalidOperationException("Transaction must have a valid signatory.");

        Console.WriteLine($"[NewGroupFeed] Processing new group feed transaction");
        Console.WriteLine($"[NewGroupFeed] FeedId: {payload.FeedId.Value.ToString().Substring(0, 8)}..., Title: {payload.Title}");
        Console.WriteLine($"[NewGroupFeed] CurrentBlock: {currentBlock.Value}, Creator: {creatorAddress.Substring(0, 10)}...");

        // Create the GroupFeed entity
        var groupFeed = new GroupFeed(
            payload.FeedId,
            payload.Title,
            payload.Description,
            payload.IsPublic,
            currentBlock,
            CurrentKeyGeneration: 0);

        // Create KeyGeneration 0 entity
        var keyGeneration = new GroupFeedKeyGenerationEntity(
            payload.FeedId,
            KeyGeneration: 0,
            currentBlock,
            RotationTrigger.Join) // Join for initial creation
        {
            GroupFeed = groupFeed
        };

        groupFeed.KeyGenerations.Add(keyGeneration);

        // Create participant entities with roles
        var participantAddresses = new List<string>();
        foreach (var participant in payload.Participants)
        {
            // Creator gets Admin role, others get Member role
            var participantType = participant.ParticipantPublicAddress == creatorAddress
                ? ParticipantType.Admin
                : ParticipantType.Member;

            var participantEntity = new GroupFeedParticipantEntity(
                payload.FeedId,
                participant.ParticipantPublicAddress,
                participantType,
                currentBlock,
                LeftAtBlock: null,
                LastLeaveBlock: null)
            {
                GroupFeed = groupFeed
            };

            groupFeed.Participants.Add(participantEntity);

            // Create encrypted key entity for this participant
            var encryptedKeyEntity = new GroupFeedEncryptedKeyEntity(
                payload.FeedId,
                KeyGeneration: 0,
                participant.ParticipantPublicAddress,
                participant.EncryptedFeedKey)
            {
                KeyGenerationEntity = keyGeneration
            };

            keyGeneration.EncryptedKeys.Add(encryptedKeyEntity);
            participantAddresses.Add(participant.ParticipantPublicAddress);
        }

        // Store the group feed with all related entities
        await this._feedsStorageService.CreateGroupFeed(groupFeed);

        // Generate invite code for public groups immediately so it's available right away
        if (payload.IsPublic)
        {
            var inviteCode = await this._feedsStorageService.GenerateInviteCodeAsync(payload.FeedId);
            Console.WriteLine($"[NewGroupFeed] Generated invite code: {inviteCode}");
        }

        Console.WriteLine($"[NewGroupFeed] GroupFeed saved with {groupFeed.Participants.Count} participants");
        foreach (var p in groupFeed.Participants)
        {
            Console.WriteLine($"[NewGroupFeed] Participant: {p.ParticipantPublicAddress.Substring(0, 10)}... Type: {p.ParticipantType}, JoinedAtBlock: {p.JoinedAtBlock.Value}");
        }

        // Publish event for other modules (e.g., Reactions) to handle
        // Fire and forget - don't block feed creation on reaction setup
        _ = this._eventAggregator.PublishAsync(new FeedCreatedEvent(
            payload.FeedId,
            participantAddresses.ToArray(),
            FeedType.Group));

        // Update all participants' feed list caches (FEAT-049)
        // Cache updates are fire-and-forget - failure does not affect the transaction
        foreach (var participantAddress in participantAddresses)
        {
            await this._userFeedsCacheService.AddFeedToUserCacheAsync(participantAddress, payload.FeedId);
        }
    }
}
