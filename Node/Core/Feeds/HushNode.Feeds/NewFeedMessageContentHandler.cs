using HushNode.Caching;
using HushNode.Credentials;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class NewFeedMessageContentHandler(
    ICredentialsProvider credentialProvider,
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache) : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;

    /// <summary>
    /// Grace period in blocks for accepting messages with the previous KeyGeneration.
    /// </summary>
    private const int GracePeriodBlocks = 5;

    public bool CanValidate(Guid transactionKind) =>
        NewFeedMessagePayloadHandler.NewFeedMessagePayloadKind == transactionKind;

    public AbstractTransaction? ValidateAndSign(AbstractTransaction transaction)
    {
        var newFeedMessagePayload = transaction as SignedTransaction<NewFeedMessagePayload>;

        if (newFeedMessagePayload == null)
        {
            return null;
        }

        var payload = newFeedMessagePayload.Payload;
        var targetGroupFeed = this._feedsStorageService.GetGroupFeedAsync(payload.FeedId).GetAwaiter().GetResult();

        // If target feed is a group, KeyGeneration is mandatory and group validation applies.
        if (targetGroupFeed != null)
        {
            if (payload.KeyGeneration == null)
            {
                return null;
            }

            var senderAddress = newFeedMessagePayload.UserSignature?.Signatory;

            if (string.IsNullOrEmpty(senderAddress))
            {
                return null;
            }

            // Validation: Check group exists and is not deleted
            if (targetGroupFeed.IsDeleted)
            {
                return null;
            }

            // Circle feeds (InnerCircle + custom circles) are audience containers, not chat channels.
            // Any message transaction targeting them must be rejected.
            if (targetGroupFeed.IsInnerCircle || !string.IsNullOrWhiteSpace(targetGroupFeed.OwnerPublicAddress))
            {
                return null;
            }

            // Validation: Check sender is an active member who can send messages
            var canSendMessages = this._feedsStorageService
                .CanMemberSendMessagesAsync(payload.FeedId, senderAddress)
                .GetAwaiter().GetResult();

            if (!canSendMessages)
            {
                return null;
            }

            // Validation: Check KeyGeneration
            if (!ValidateKeyGeneration(payload.KeyGeneration.Value, targetGroupFeed.CurrentKeyGeneration, payload.FeedId))
            {
                return null;
            }

            // Validation: AuthorCommitment must be exactly 32 bytes if provided (Protocol Omega)
            if (payload.AuthorCommitment != null && payload.AuthorCommitment.Length != 32)
            {
                return null;
            }
        }

        var blockProducerCredentials = this._credentialProvider.GetCredentials();

        var signedByValidationTransaction = newFeedMessagePayload.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);

        return signedByValidationTransaction;
    }

    /// <summary>
    /// Validates the KeyGeneration of a group message.
    /// - Current KeyGeneration is always accepted
    /// - Previous KeyGeneration (N-1) is accepted within grace period
    /// - Future KeyGenerations (N+1 or higher) are rejected
    /// - Old KeyGenerations (N-2 or older) are rejected
    /// </summary>
    private bool ValidateKeyGeneration(int messageKeyGen, int currentKeyGen, FeedId feedId)
    {
        if (messageKeyGen == currentKeyGen)
        {
            return true;
        }

        if (messageKeyGen > currentKeyGen)
        {
            return false;
        }

        if (messageKeyGen < currentKeyGen - 1)
        {
            return false;
        }

        // Previous KeyGeneration (N-1) - check grace period
        var currentKeyGenEntity = this._feedsStorageService
            .GetKeyGenerationByNumberAsync(feedId, currentKeyGen)
            .GetAwaiter().GetResult();

        if (currentKeyGenEntity == null)
        {
            return false;
        }

        var currentBlockIndex = this._blockchainCache.LastBlockIndex;
        var keyGenValidFromBlock = currentKeyGenEntity.ValidFromBlock;

        var blocksSinceKeyRotation = currentBlockIndex.Value - keyGenValidFromBlock.Value;
        return blocksSinceKeyRotation < GracePeriodBlocks;
    }
}
