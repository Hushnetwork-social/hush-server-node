using System.Numerics;
using Microsoft.Extensions.Logging;
using HushNode.Credentials;
using HushNode.Reactions.Crypto;
using HushNode.Reactions.ZK;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Reactions.Model;

namespace HushNode.Reactions;

/// <summary>
/// Validates reaction transactions and signs them with the block producer's key.
/// Performs ZK proof verification before accepting the transaction.
/// </summary>
public class NewReactionContentHandler : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider;
    private readonly IZkVerifier _zkVerifier;
    private readonly IMembershipService _membershipService;
    private readonly IFeedInfoProvider _feedInfoProvider;
    private readonly ILogger<NewReactionContentHandler> _logger;

    // Grace period: accept proofs against recent N Merkle roots
    private const int MerkleRootGracePeriod = 3;

    public NewReactionContentHandler(
        ICredentialsProvider credentialProvider,
        IZkVerifier zkVerifier,
        IMembershipService membershipService,
        IFeedInfoProvider feedInfoProvider,
        ILogger<NewReactionContentHandler> logger)
    {
        _credentialProvider = credentialProvider;
        _zkVerifier = zkVerifier;
        _membershipService = membershipService;
        _feedInfoProvider = feedInfoProvider;
        _logger = logger;
    }

    public bool CanValidate(Guid transactionKind) =>
        NewReactionPayloadHandler.NewReactionPayloadKind == transactionKind;

    public AbstractTransaction ValidateAndSign(AbstractTransaction transaction)
    {
        var reactionTransaction = transaction as SignedTransaction<NewReactionPayload>;

        if (reactionTransaction == null)
        {
            _logger.LogWarning("[NewReactionContentHandler] Invalid transaction type");
            return null;
        }

        var payload = reactionTransaction.Payload;

        // Validate input sizes
        if (payload.CiphertextC1X.Length != 6 || payload.CiphertextC1Y.Length != 6 ||
            payload.CiphertextC2X.Length != 6 || payload.CiphertextC2Y.Length != 6)
        {
            _logger.LogWarning("[NewReactionContentHandler] Invalid ciphertext size for message {MessageId}", payload.MessageId);
            return null;
        }

        // DEV MODE: Skip ZK verification if circuit version indicates dev mode
        var isDevMode = payload.CircuitVersion?.StartsWith("dev-mode") == true;

        if (isDevMode)
        {
            _logger.LogWarning("[NewReactionContentHandler] DEV MODE - skipping ZK verification for message {MessageId}", payload.MessageId);
        }
        else
        {
            // Verify ZK proof
            var verificationResult = VerifyZkProofAsync(payload).GetAwaiter().GetResult();
            if (!verificationResult)
            {
                _logger.LogWarning("[NewReactionContentHandler] ZK proof verification failed for message {MessageId}", payload.MessageId);
                return null;
            }
        }

        // Sign with block producer credentials
        var blockProducerCredentials = _credentialProvider.GetCredentials();

        var signedByValidatorTransaction = reactionTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);

        _logger.LogInformation("[NewReactionContentHandler] Validated reaction for message {MessageId}", payload.MessageId);

        return signedByValidatorTransaction;
    }

    private async Task<bool> VerifyZkProofAsync(NewReactionPayload payload)
    {
        try
        {
            // Get feed public key
            var feedPk = await _feedInfoProvider.GetFeedPublicKeyAsync(payload.FeedId);
            if (feedPk == null)
            {
                _logger.LogWarning("[NewReactionContentHandler] Feed not found: {FeedId}", payload.FeedId);
                return false;
            }

            // Get author commitment for the message
            var authorCommitment = await _feedInfoProvider.GetAuthorCommitmentAsync(payload.MessageId);
            if (authorCommitment == null)
            {
                _logger.LogWarning("[NewReactionContentHandler] Message not found or no author commitment: {MessageId}", payload.MessageId);
                return false;
            }

            // Get recent Merkle roots for grace period verification
            var recentRoots = await _membershipService.GetRecentRootsAsync(payload.FeedId, MerkleRootGracePeriod);
            if (!recentRoots.Any())
            {
                _logger.LogWarning("[NewReactionContentHandler] No Merkle roots found for feed {FeedId}", payload.FeedId);
                return false;
            }

            // Convert payload ciphertexts to ECPoints
            var c1Points = new ECPoint[6];
            var c2Points = new ECPoint[6];
            for (int i = 0; i < 6; i++)
            {
                c1Points[i] = ECPoint.FromCoordinates(payload.CiphertextC1X[i], payload.CiphertextC1Y[i]);
                c2Points[i] = ECPoint.FromCoordinates(payload.CiphertextC2X[i], payload.CiphertextC2Y[i]);
            }

            // Verify against each recent root (grace period)
            foreach (var rootInfo in recentRoots)
            {
                var publicInputs = new PublicInputs
                {
                    Nullifier = payload.Nullifier,
                    CiphertextC1 = c1Points,
                    CiphertextC2 = c2Points,
                    MessageId = payload.MessageId.Value.ToByteArray(),
                    FeedPk = feedPk,
                    MembersRoot = rootInfo.MerkleRoot,
                    AuthorCommitment = new BigInteger(authorCommitment, isUnsigned: true, isBigEndian: true)
                };

                var verifyResult = await _zkVerifier.VerifyAsync(payload.ZkProof, publicInputs, payload.CircuitVersion);
                if (verifyResult.Valid)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NewReactionContentHandler] Error verifying ZK proof for message {MessageId}", payload.MessageId);
            return false;
        }
    }
}
