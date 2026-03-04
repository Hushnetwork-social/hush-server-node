using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushNode.Feeds;

public class AddMembersToCustomCircleTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache,
    IKeyRotationService keyRotationService,
    IUserFeedsCacheService userFeedsCacheService,
    IFeedParticipantsCacheService feedParticipantsCacheService,
    IGroupMembersCacheService groupMembersCacheService,
    ILogger<AddMembersToCustomCircleTransactionHandler> logger)
    : IAddMembersToCustomCircleTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IKeyRotationService _keyRotationService = keyRotationService;
    private readonly IUserFeedsCacheService _userFeedsCacheService = userFeedsCacheService;
    private readonly IFeedParticipantsCacheService _feedParticipantsCacheService = feedParticipantsCacheService;
    private readonly IGroupMembersCacheService _groupMembersCacheService = groupMembersCacheService;
    private readonly ILogger<AddMembersToCustomCircleTransactionHandler> _logger = logger;

    public async Task HandleAddMembersToCustomCircleTransactionAsync(ValidatedTransaction<AddMembersToCustomCirclePayload> transaction)
    {
        var payload = transaction.Payload;
        var currentBlock = this._blockchainCache.LastBlockIndex;

        this._logger.LogInformation(
            "custom_circle.add_members.requested owner={Owner} feedId={FeedId} count={Count}",
            payload.OwnerPublicAddress,
            payload.FeedId,
            payload.Members.Length);

        var customCircle = await this._feedsStorageService.GetGroupFeedAsync(payload.FeedId);
        if (customCircle == null || customCircle.IsDeleted || customCircle.IsInnerCircle || customCircle.OwnerPublicAddress != payload.OwnerPublicAddress)
        {
            throw new InvalidOperationException("CUSTOM_CIRCLE_NOT_FOUND_OR_UNAUTHORIZED");
        }

        var participantsToAdd = new List<GroupFeedParticipantEntity>();
        var participantsToRejoin = new List<string>();

        foreach (var member in payload.Members)
        {
            var isFollowedByOwner = await this._feedsStorageService.OwnerHasChatFeedWithMemberAsync(payload.OwnerPublicAddress, member.PublicAddress);
            if (!isFollowedByOwner)
            {
                throw new InvalidOperationException("CUSTOM_CIRCLE_MEMBER_NOT_FOLLOWED");
            }

            var existingParticipant = await this._feedsStorageService.GetParticipantWithHistoryAsync(customCircle.FeedId, member.PublicAddress);
            if (existingParticipant == null)
            {
                participantsToAdd.Add(new GroupFeedParticipantEntity(
                    customCircle.FeedId,
                    member.PublicAddress,
                    ParticipantType.Member,
                    currentBlock,
                    LeftAtBlock: null,
                    LastLeaveBlock: null));
            }
            else if (existingParticipant.LeftAtBlock != null)
            {
                participantsToRejoin.Add(member.PublicAddress);
            }
        }

        var joiningAddresses = payload.Members.Select(x => x.PublicAddress).ToArray();
        var keyRotationResult = await this._keyRotationService.TriggerRotationAsync(
            customCircle.FeedId,
            RotationTrigger.Join,
            joiningAddresses,
            leavingMemberAddresses: null);

        if (!keyRotationResult.IsSuccess || keyRotationResult.Payload == null)
        {
            throw new InvalidOperationException($"CUSTOM_CIRCLE_ROTATION_FAILED:{keyRotationResult.ErrorMessage}");
        }

        var payloadRotation = keyRotationResult.Payload;
        var keyGenerationEntity = new GroupFeedKeyGenerationEntity(
            payloadRotation.FeedId,
            payloadRotation.NewKeyGeneration,
            new HushShared.Blockchain.BlockModel.BlockIndex(payloadRotation.ValidFromBlock),
            payloadRotation.RotationTrigger);

        foreach (var encryptedKey in payloadRotation.EncryptedKeys)
        {
            keyGenerationEntity.EncryptedKeys.Add(new GroupFeedEncryptedKeyEntity(
                payloadRotation.FeedId,
                payloadRotation.NewKeyGeneration,
                encryptedKey.MemberPublicAddress,
                encryptedKey.EncryptedAesKey)
            {
                KeyGenerationEntity = keyGenerationEntity
            });
        }

        await this._feedsStorageService.ApplyInnerCircleMembershipAndKeyRotationAsync(
            customCircle.FeedId,
            participantsToAdd,
            participantsToRejoin,
            currentBlock,
            keyGenerationEntity,
            currentBlock);

        await this._feedParticipantsCacheService.InvalidateKeyGenerationsAsync(customCircle.FeedId);
        await this._groupMembersCacheService.InvalidateGroupMembersAsync(customCircle.FeedId);

        foreach (var member in payload.Members)
        {
            await this._feedParticipantsCacheService.AddParticipantAsync(customCircle.FeedId, member.PublicAddress);
            await this._userFeedsCacheService.AddFeedToUserCacheAsync(member.PublicAddress, customCircle.FeedId);
        }

        this._logger.LogInformation(
            "custom_circle.add_members.succeeded owner={Owner} feedId={FeedId} count={Count} keyGeneration={KeyGeneration}",
            payload.OwnerPublicAddress,
            customCircle.FeedId,
            payload.Members.Length,
            payloadRotation.NewKeyGeneration);
    }
}
