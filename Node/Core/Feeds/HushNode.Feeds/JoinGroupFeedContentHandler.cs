using HushNode.Caching;
using HushNode.Credentials;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Content handler that validates JoinGroupFeed transactions.
/// Returns null on validation failure (existing pattern).
/// </summary>
public class JoinGroupFeedContentHandler(
    ICredentialsProvider credentialProvider,
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache)
    : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;

    /// <summary>
    /// Cooldown period in blocks after leaving before rejoining is allowed.
    /// </summary>
    public const int CooldownBlocks = 100;

    public bool CanValidate(Guid transactionKind) =>
        JoinGroupFeedPayloadHandler.JoinGroupFeedPayloadKind == transactionKind;

    public AbstractTransaction ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<JoinGroupFeedPayload>;

        if (signedTransaction == null)
        {
            return null!;
        }

        var payload = signedTransaction.Payload;
        var joiningUserAddress = payload.JoiningUserPublicAddress;

        // Validation: Joining user address must match signatory
        var signatoryAddress = signedTransaction.UserSignature?.Signatory;
        if (string.IsNullOrEmpty(signatoryAddress) || signatoryAddress != joiningUserAddress)
        {
            return null!;
        }

        // Get the group feed
        var groupFeed = this._feedsStorageService.GetGroupFeedAsync(payload.FeedId).GetAwaiter().GetResult();
        if (groupFeed == null)
        {
            return null!; // Group doesn't exist
        }

        // Validation: Cannot join deleted group
        if (groupFeed.IsDeleted)
        {
            return null!;
        }

        // Validation: Public groups only (private requires invitation - future implementation)
        if (!groupFeed.IsPublic && string.IsNullOrEmpty(payload.InvitationSignature))
        {
            return null!; // Private group requires invitation
        }

        // Check participant history (includes those who left)
        var existingParticipant = this._feedsStorageService
            .GetParticipantWithHistoryAsync(payload.FeedId, joiningUserAddress)
            .GetAwaiter().GetResult();

        if (existingParticipant != null)
        {
            // Validation: Cannot join if banned
            if (existingParticipant.ParticipantType == ParticipantType.Banned)
            {
                return null!;
            }

            // Validation: Cannot join if already an active member
            if (existingParticipant.LeftAtBlock == null)
            {
                return null!;
            }

            // Validation: Cooldown check for rejoining
            if (existingParticipant.LastLeaveBlock != null)
            {
                var currentBlock = this._blockchainCache.LastBlockIndex;
                var blocksSinceLeave = currentBlock.Value - existingParticipant.LastLeaveBlock.Value;
                if (blocksSinceLeave < CooldownBlocks)
                {
                    return null!; // Still in cooldown period
                }
            }
        }

        // All validations passed - sign the transaction
        var blockProducerCredentials = this._credentialProvider.GetCredentials();

        var signedByValidationTransaction = signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);

        return signedByValidationTransaction;
    }
}
