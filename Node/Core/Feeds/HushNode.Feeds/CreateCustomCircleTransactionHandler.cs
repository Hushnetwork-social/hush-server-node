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

public class CreateCustomCircleTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IIdentityStorageService identityStorageService,
    IBlockchainCache blockchainCache,
    IUserFeedsCacheService userFeedsCacheService,
    IFeedMetadataCacheService feedMetadataCacheService,
    IEventAggregator eventAggregator,
    ILogger<CreateCustomCircleTransactionHandler> logger)
    : ICreateCustomCircleTransactionHandler
{
    private const int MaxCustomCirclesPerOwner = 20;

    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IIdentityStorageService _identityStorageService = identityStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IUserFeedsCacheService _userFeedsCacheService = userFeedsCacheService;
    private readonly IFeedMetadataCacheService _feedMetadataCacheService = feedMetadataCacheService;
    private readonly IEventAggregator _eventAggregator = eventAggregator;
    private readonly ILogger<CreateCustomCircleTransactionHandler> _logger = logger;

    public async Task HandleCreateCustomCircleTransactionAsync(ValidatedTransaction<CreateCustomCirclePayload> transaction)
    {
        var payload = transaction.Payload;
        var ownerAddress = payload.OwnerPublicAddress;
        var currentBlock = this._blockchainCache.LastBlockIndex;

        if (!CustomCircleNameRules.TryNormalize(payload.CircleName, out var trimmedCircleName, out var normalizedCircleName))
        {
            throw new InvalidOperationException("CUSTOM_CIRCLE_INVALID_NAME");
        }

        this._logger.LogInformation(
            "custom_circle.create.requested owner={Owner} feedId={FeedId} name={CircleName}",
            ownerAddress,
            payload.FeedId,
            normalizedCircleName);

        var ownerIdentity = await this._identityStorageService.RetrieveIdentityAsync(ownerAddress);
        if (ownerIdentity is not Profile ownerProfile || string.IsNullOrWhiteSpace(ownerProfile.PublicEncryptAddress))
        {
            throw new InvalidOperationException("CUSTOM_CIRCLE_OWNER_IDENTITY_NOT_FOUND");
        }

        var ownerCircleCount = await this._feedsStorageService.GetCustomCircleCountByOwnerAsync(ownerAddress);
        if (ownerCircleCount >= MaxCustomCirclesPerOwner)
        {
            throw new InvalidOperationException("CUSTOM_CIRCLE_OWNER_LIMIT_REACHED");
        }

        var alreadyExists = await this._feedsStorageService.OwnerHasCustomCircleNamedAsync(ownerAddress, normalizedCircleName);
        if (alreadyExists)
        {
            throw new InvalidOperationException("CUSTOM_CIRCLE_ALREADY_EXISTS");
        }

        var encryptedOwnerKey = EncryptKeys.Encrypt(EncryptKeys.GenerateAesKey(), ownerProfile.PublicEncryptAddress);
        var feedId = payload.FeedId;

        var groupFeed = new GroupFeed(
            FeedId: feedId,
            Title: trimmedCircleName,
            Description: "Owner-managed custom circle",
            IsPublic: false,
            CreatedAtBlock: currentBlock,
            CurrentKeyGeneration: 0,
            IsInnerCircle: false,
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
                Title = trimmedCircleName,
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

        this._logger.LogInformation(
            "custom_circle.create.succeeded owner={Owner} feedId={FeedId} name={CircleName}",
            ownerAddress,
            feedId,
            normalizedCircleName);
    }
}
