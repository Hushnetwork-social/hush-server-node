using HushNode.Caching;
using HushNode.Feeds.Events;
using HushNode.Feeds.Storage;
using HushNode.Identity.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushNode.Feeds;

public class CreateInnerCircleTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IIdentityStorageService identityStorageService,
    IBlockchainCache blockchainCache,
    IUserFeedsCacheService userFeedsCacheService,
    IFeedMetadataCacheService feedMetadataCacheService,
    IEventAggregator eventAggregator,
    ILogger<CreateInnerCircleTransactionHandler> logger)
    : ICreateInnerCircleTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IIdentityStorageService _identityStorageService = identityStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IUserFeedsCacheService _userFeedsCacheService = userFeedsCacheService;
    private readonly IFeedMetadataCacheService _feedMetadataCacheService = feedMetadataCacheService;
    private readonly IEventAggregator _eventAggregator = eventAggregator;
    private readonly ILogger<CreateInnerCircleTransactionHandler> _logger = logger;

    public async Task HandleCreateInnerCircleTransactionAsync(ValidatedTransaction<CreateInnerCirclePayload> transaction)
    {
        var ownerAddress = transaction.Payload.OwnerPublicAddress;
        var currentBlock = this._blockchainCache.LastBlockIndex;

        this._logger.LogInformation("inner_circle.create.requested owner={Owner}", ownerAddress);

        var ownerIdentity = await this._identityStorageService.RetrieveIdentityAsync(ownerAddress);
        if (ownerIdentity is not Profile ownerProfile || string.IsNullOrWhiteSpace(ownerProfile.PublicEncryptAddress))
        {
            this._logger.LogWarning("inner_circle.create.failed owner={Owner} code=INNER_CIRCLE_OWNER_IDENTITY_NOT_FOUND", ownerAddress);
            throw new InvalidOperationException("INNER_CIRCLE_OWNER_IDENTITY_NOT_FOUND");
        }

        var feedId = FeedId.NewFeedId;
        var encryptedOwnerKey = EncryptKeys.Encrypt(EncryptKeys.GenerateAesKey(), ownerProfile.PublicEncryptAddress);

        var groupFeed = new GroupFeed(
            FeedId: feedId,
            Title: "Inner Circle",
            Description: "Auto-managed inner circle",
            IsPublic: false,
            CreatedAtBlock: currentBlock,
            CurrentKeyGeneration: 0,
            IsInnerCircle: true,
            OwnerPublicAddress: ownerAddress);

        var keyGeneration = new GroupFeedKeyGenerationEntity(
            feedId,
            KeyGeneration: 0,
            currentBlock,
            RotationTrigger.Join)
        {
            GroupFeed = groupFeed
        };

        groupFeed.KeyGenerations.Add(keyGeneration);

        var ownerParticipant = new GroupFeedParticipantEntity(
            feedId,
            ownerAddress,
            ParticipantType.Owner,
            currentBlock,
            LeftAtBlock: null,
            LastLeaveBlock: null)
        {
            GroupFeed = groupFeed
        };

        groupFeed.Participants.Add(ownerParticipant);

        keyGeneration.EncryptedKeys.Add(new GroupFeedEncryptedKeyEntity(
            feedId,
            KeyGeneration: 0,
            MemberPublicAddress: ownerAddress,
            EncryptedAesKey: encryptedOwnerKey)
        {
            KeyGenerationEntity = keyGeneration
        });

        await this._feedsStorageService.CreateGroupFeed(groupFeed);

        await this._userFeedsCacheService.AddFeedToUserCacheAsync(ownerAddress, feedId);

        _ = this._feedMetadataCacheService.SetFeedMetadataAsync(
            ownerAddress,
            feedId,
            new FeedMetadataEntry
            {
                Title = "Inner Circle",
                Type = (int)FeedType.Group,
                LastBlockIndex = currentBlock.Value,
                Participants = new List<string> { ownerAddress },
                CreatedAtBlock = currentBlock.Value,
                CurrentKeyGeneration = 0
            });

        _ = this._eventAggregator.PublishAsync(new FeedCreatedEvent(
            feedId,
            new[] { ownerAddress },
            FeedType.Group));

        this._logger.LogInformation("inner_circle.create.succeeded owner={Owner} feedId={FeedId}", ownerAddress, feedId);
    }
}
