using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushNode.Feeds;

public class AddMembersToInnerCircleTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache,
    IKeyRotationService keyRotationService,
    IUserFeedsCacheService userFeedsCacheService,
    IFeedParticipantsCacheService feedParticipantsCacheService,
    IGroupMembersCacheService groupMembersCacheService,
    ILogger<AddMembersToInnerCircleTransactionHandler> logger)
    : IAddMembersToInnerCircleTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IKeyRotationService _keyRotationService = keyRotationService;
    private readonly IUserFeedsCacheService _userFeedsCacheService = userFeedsCacheService;
    private readonly IFeedParticipantsCacheService _feedParticipantsCacheService = feedParticipantsCacheService;
    private readonly IGroupMembersCacheService _groupMembersCacheService = groupMembersCacheService;
    private readonly ILogger<AddMembersToInnerCircleTransactionHandler> _logger = logger;

    public async Task HandleAddMembersToInnerCircleTransactionAsync(ValidatedTransaction<AddMembersToInnerCirclePayload> transaction)
    {
        var payload = transaction.Payload;
        var currentBlock = this._blockchainCache.LastBlockIndex;

        this._logger.LogInformation(
            "inner_circle.add_members.requested owner={Owner} count={Count}",
            payload.OwnerPublicAddress,
            payload.Members.Length);

        var innerCircle = await this._feedsStorageService.GetInnerCircleByOwnerAsync(payload.OwnerPublicAddress);
        if (innerCircle == null || innerCircle.IsDeleted)
        {
            this._logger.LogWarning(
                "inner_circle.add_members.failed owner={Owner} code=INNER_CIRCLE_NOT_FOUND",
                payload.OwnerPublicAddress);
            throw new InvalidOperationException("INNER_CIRCLE_NOT_FOUND");
        }

        var participantsToAdd = new List<GroupFeedParticipantEntity>();
        var participantsToRejoin = new List<string>();

        foreach (var member in payload.Members)
        {
            var existingParticipant = await this._feedsStorageService
                .GetParticipantWithHistoryAsync(innerCircle.FeedId, member.PublicAddress);

            if (existingParticipant == null)
            {
                participantsToAdd.Add(new GroupFeedParticipantEntity(
                    innerCircle.FeedId,
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
            innerCircle.FeedId,
            RotationTrigger.Join,
            joiningAddresses,
            leavingMemberAddresses: null);

        if (!keyRotationResult.IsSuccess || keyRotationResult.Payload == null)
        {
            this._logger.LogWarning(
                "inner_circle.add_members.failed owner={Owner} code=INNER_CIRCLE_ROTATION_FAILED reason={Reason}",
                payload.OwnerPublicAddress,
                keyRotationResult.ErrorMessage ?? "unknown");
            throw new InvalidOperationException($"INNER_CIRCLE_ROTATION_FAILED:{keyRotationResult.ErrorMessage}");
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
            innerCircle.FeedId,
            participantsToAdd,
            participantsToRejoin,
            currentBlock,
            keyGenerationEntity,
            currentBlock);

        await this._feedParticipantsCacheService.InvalidateKeyGenerationsAsync(innerCircle.FeedId);
        await this._groupMembersCacheService.InvalidateGroupMembersAsync(innerCircle.FeedId);

        foreach (var member in payload.Members)
        {
            await this._feedParticipantsCacheService.AddParticipantAsync(innerCircle.FeedId, member.PublicAddress);
            await this._userFeedsCacheService.AddFeedToUserCacheAsync(member.PublicAddress, innerCircle.FeedId);
        }

        this._logger.LogInformation(
            "inner_circle.add_members.succeeded owner={Owner} feedId={FeedId} count={Count} keyGeneration={KeyGeneration}",
            payload.OwnerPublicAddress,
            innerCircle.FeedId,
            payload.Members.Length,
            payloadRotation.NewKeyGeneration);
    }
}
