using HushNode.Caching;
using HushNode.Credentials;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Content handler for NewGroupFeedMessage transactions.
/// Validates that:
/// - Group exists and is not deleted
/// - Sender is an active member (Admin or Member, not Blocked/Banned/Left)
/// - KeyGeneration is current or within grace period (5 blocks)
/// </summary>
public class NewGroupFeedMessageContentHandler(
    ICredentialsProvider credentialProvider,
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache)
    : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;

    /// <summary>
    /// Grace period in blocks for accepting messages with the previous KeyGeneration.
    /// </summary>
    private const int GracePeriodBlocks = 5;

    public bool CanValidate(Guid transactionKind) =>
        NewGroupFeedMessagePayloadHandler.NewGroupFeedMessagePayloadKind == transactionKind;

    public AbstractTransaction? ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<NewGroupFeedMessagePayload>;

        if (signedTransaction == null)
        {
            return null;
        }

        var payload = signedTransaction.Payload;
        var senderAddress = signedTransaction.UserSignature?.Signatory;

        // Validation: Sender address is required
        if (string.IsNullOrEmpty(senderAddress))
        {
            return null;
        }

        // Validation: Check group exists and is not deleted
        var groupFeed = this._feedsStorageService.GetGroupFeedAsync(payload.FeedId).GetAwaiter().GetResult();
        if (groupFeed == null)
        {
            return null; // Group not found
        }

        if (groupFeed.IsDeleted)
        {
            return null; // Group has been deleted
        }

        // Validation: Check sender is an active member who can send messages
        var canSendMessages = this._feedsStorageService
            .CanMemberSendMessagesAsync(payload.FeedId, senderAddress)
            .GetAwaiter().GetResult();

        if (!canSendMessages)
        {
            return null; // Not a member, Blocked, Banned, or Left
        }

        // Validation: Check KeyGeneration
        var keyGenValidationResult = ValidateKeyGeneration(
            payload.KeyGeneration,
            groupFeed.CurrentKeyGeneration,
            payload.FeedId);

        if (!keyGenValidationResult)
        {
            return null;
        }

        // All validations passed - sign the transaction
        var blockProducerCredentials = this._credentialProvider.GetCredentials();

        var signedByValidationTransaction = signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);

        return signedByValidationTransaction;
    }

    /// <summary>
    /// Validates the KeyGeneration of the message.
    /// - Current KeyGeneration is always accepted
    /// - Previous KeyGeneration (N-1) is accepted within grace period
    /// - Future KeyGenerations (N+1 or higher) are rejected
    /// - Old KeyGenerations (N-2 or older) are rejected
    /// </summary>
    private bool ValidateKeyGeneration(int messageKeyGen, int currentKeyGen, FeedId feedId)
    {
        // Accept current KeyGeneration
        if (messageKeyGen == currentKeyGen)
        {
            return true;
        }

        // Reject future KeyGeneration
        if (messageKeyGen > currentKeyGen)
        {
            return false;
        }

        // Reject very old KeyGeneration (N-2 or older)
        if (messageKeyGen < currentKeyGen - 1)
        {
            return false;
        }

        // Previous KeyGeneration (N-1) - check grace period
        // Get when the current KeyGeneration became active
        var currentKeyGenEntity = this._feedsStorageService
            .GetKeyGenerationByNumberAsync(feedId, currentKeyGen)
            .GetAwaiter().GetResult();

        if (currentKeyGenEntity == null)
        {
            // Shouldn't happen, but reject if we can't verify
            return false;
        }

        var currentBlockIndex = this._blockchainCache.LastBlockIndex;
        var keyGenValidFromBlock = currentKeyGenEntity.ValidFromBlock;

        // Check if within grace period
        var blocksSinceKeyRotation = currentBlockIndex.Value - keyGenValidFromBlock.Value;
        return blocksSinceKeyRotation < GracePeriodBlocks;
    }
}
