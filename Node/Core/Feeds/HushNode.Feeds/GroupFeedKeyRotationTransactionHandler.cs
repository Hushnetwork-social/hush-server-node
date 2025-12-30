using HushNode.Feeds.Events;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using Olimpo;

namespace HushNode.Feeds;

/// <summary>
/// Handler for persisting GroupFeedKeyRotation transactions to the database.
/// Creates a new KeyGeneration entity with all encrypted member keys, and updates
/// the group's CurrentKeyGeneration field atomically.
/// </summary>
public class GroupFeedKeyRotationTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IEventAggregator eventAggregator)
    : IGroupFeedKeyRotationTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IEventAggregator _eventAggregator = eventAggregator;

    public async Task HandleKeyRotationTransactionAsync(
        ValidatedTransaction<GroupFeedKeyRotationPayload> keyRotationTransaction)
    {
        var payload = keyRotationTransaction.Payload;

        // Create the KeyGeneration entity
        var keyGenerationEntity = new GroupFeedKeyGenerationEntity(
            payload.FeedId,
            payload.NewKeyGeneration,
            new BlockIndex(payload.ValidFromBlock),
            payload.RotationTrigger);

        // Map payload encrypted keys to entity encrypted keys
        var encryptedKeyEntities = payload.EncryptedKeys
            .Select(ek => new GroupFeedEncryptedKeyEntity(
                payload.FeedId,
                payload.NewKeyGeneration,
                ek.MemberPublicAddress,
                ek.EncryptedAesKey))
            .ToList();

        // Add encrypted keys to the key generation entity
        foreach (var encryptedKey in encryptedKeyEntities)
        {
            keyGenerationEntity.EncryptedKeys.Add(encryptedKey);
        }

        // Persist atomically: creates KeyGeneration + encrypted keys + updates group's CurrentKeyGeneration
        await this._feedsStorageService.CreateKeyRotationAsync(keyGenerationEntity);

        // Publish event for client notification
        // Fire and forget - don't block key rotation on downstream processing
        var memberAddresses = payload.EncryptedKeys
            .Select(ek => ek.MemberPublicAddress)
            .ToArray();

        _ = this._eventAggregator.PublishAsync(new KeyRotationCompletedEvent(
            payload.FeedId,
            payload.NewKeyGeneration,
            memberAddresses,
            payload.RotationTrigger));
    }
}
