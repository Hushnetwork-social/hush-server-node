using System.Numerics;
using Microsoft.Extensions.Logging;
using HushNode.Credentials;
using HushNode.MemPool;
using HushNode.Reactions.Crypto;
using HushNode.Reactions.ZK;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Reactions.Model;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Globalization;

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
    private readonly IMemPoolService _memPoolService;
    private readonly ILogger<NewReactionContentHandler> _logger;

    // Grace period: accept proofs against recent N Merkle roots
    private const int MerkleRootGracePeriod = 3;
    private const int CoordinateLengthBytes = 32;
    private const int LegacyBackupLengthBytes = 1;
    private const int VersionedBackupLengthBytes = 32;
    private static readonly byte[] ZeroBytes32 = new byte[32];
    private static readonly Regex SupportedCircuitVersionPattern = new(@"^omega-v\d+\.\d+\.\d+$|^dev-mode-v\d+$", RegexOptions.Compiled);
    private sealed record RootDebugSnapshot(long BlockHeight, string MembersRootHex);

    public NewReactionContentHandler(
        ICredentialsProvider credentialProvider,
        IZkVerifier zkVerifier,
        IMembershipService membershipService,
        IFeedInfoProvider feedInfoProvider,
        IMemPoolService memPoolService,
        ILogger<NewReactionContentHandler> logger)
    {
        _credentialProvider = credentialProvider;
        _zkVerifier = zkVerifier;
        _membershipService = membershipService;
        _feedInfoProvider = feedInfoProvider;
        _memPoolService = memPoolService;
        _logger = logger;
    }

    public bool CanValidate(Guid transactionKind)
    {
        var canValidate = NewReactionPayloadHandler.NewReactionPayloadKind == transactionKind;
        Console.WriteLine($"[E2E Reaction] NewReactionContentHandler.CanValidate: {canValidate}, PayloadKind={transactionKind}");
        return canValidate;
    }

    public AbstractTransaction? ValidateAndSign(AbstractTransaction transaction)
    {
        Console.WriteLine($"[E2E Reaction] NewReactionContentHandler.ValidateAndSign: Starting validation for transaction {transaction.TransactionId}");

        var reactionTransaction = transaction as SignedTransaction<NewReactionPayload>;

        if (reactionTransaction == null)
        {
            Console.WriteLine("[E2E Reaction] NewReactionContentHandler.ValidateAndSign: FAILED - Invalid transaction type");
            _logger.LogWarning("[NewReactionContentHandler] Invalid transaction type");
            return null;
        }

        var payload = reactionTransaction.Payload;
        Console.WriteLine($"[E2E Reaction] NewReactionContentHandler.ValidateAndSign: MessageId={payload.MessageId}, FeedId={payload.FeedId}");

        // Validate input sizes
        if (payload.CiphertextC1X.Length != 6 || payload.CiphertextC1Y.Length != 6 ||
            payload.CiphertextC2X.Length != 6 || payload.CiphertextC2Y.Length != 6)
        {
            _logger.LogWarning("[NewReactionContentHandler] Invalid ciphertext size for message {MessageId}", payload.MessageId);
            return null;
        }

        if (!HasValidCoordinateLengths(payload))
        {
            _logger.LogWarning("[NewReactionContentHandler] Invalid ciphertext coordinate length for message {MessageId}", payload.MessageId);
            return null;
        }

        if (!HasValidBackupLength(payload.EncryptedEmojiBackup))
        {
            _logger.LogWarning("[NewReactionContentHandler] Invalid encrypted backup length for message {MessageId}", payload.MessageId);
            return null;
        }

        if (!IsSupportedCircuitVersionFormat(payload.CircuitVersion))
        {
            _logger.LogWarning("[NewReactionContentHandler] Invalid circuit version format '{CircuitVersion}' for message {MessageId}", payload.CircuitVersion, payload.MessageId);
            return null;
        }

        if (HasPendingReactionForSameTarget(reactionTransaction.UserSignature?.Signatory, payload))
        {
            var signatory = reactionTransaction.UserSignature?.Signatory;
            _logger.LogWarning(
                "[NewReactionContentHandler] Duplicate pending reaction rejected for message {MessageId} and signatory {Signatory}",
                payload.MessageId,
                signatory);
            return null;
        }

        // Always delegate proof validation to the configured verifier. Dev-mode acceptance,
        // if enabled at all, must come from explicit DI configuration instead of payload flags.
        var verificationResult = VerifyZkProofAsync(payload).GetAwaiter().GetResult();
        if (!verificationResult)
        {
            _logger.LogWarning("[NewReactionContentHandler] ZK proof verification failed for message {MessageId}", payload.MessageId);
            return null;
        }

        // Sign with block producer credentials
        Console.WriteLine("[E2E Reaction] NewReactionContentHandler.ValidateAndSign: Validation passed, signing with block producer credentials");
        var blockProducerCredentials = _credentialProvider.GetCredentials();

        var signedByValidatorTransaction = reactionTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);

        Console.WriteLine($"[E2E Reaction] NewReactionContentHandler.ValidateAndSign: SUCCESS - Transaction signed, adding to mempool");
        _logger.LogInformation("[NewReactionContentHandler] Validated reaction for message {MessageId}", payload.MessageId);

        return signedByValidatorTransaction;
    }

    private async Task<bool> VerifyZkProofAsync(NewReactionPayload payload)
    {
        try
        {
            var isExplicitDevMode = string.Equals(payload.CircuitVersion, "dev-mode-v1", StringComparison.Ordinal);

            // Get feed public key
            var feedPk = await _feedInfoProvider.GetFeedPublicKeyAsync(payload.FeedId);
            if (feedPk == null)
            {
                _logger.LogWarning("[NewReactionContentHandler] Feed not found: {FeedId}", payload.FeedId);
                return false;
            }

            // Get author commitment for the message
            var authorCommitment = await _feedInfoProvider.GetAuthorCommitmentAsync(payload.MessageId);
            if (authorCommitment == null && !isExplicitDevMode)
            {
                _logger.LogWarning("[NewReactionContentHandler] Message not found or no author commitment: {MessageId}", payload.MessageId);
                return false;
            }
            authorCommitment ??= ZeroBytes32;

            var membershipScopeId = await _feedInfoProvider.GetMembershipScopeIdAsync(payload.FeedId);
            if (membershipScopeId == null)
            {
                _logger.LogWarning("[NewReactionContentHandler] Membership scope not found for reaction scope {FeedId}", payload.FeedId);
                return false;
            }
            // Get recent Merkle roots for grace period verification
            var recentRoots = await _membershipService.GetRecentRootsAsync(membershipScopeId.Value, MerkleRootGracePeriod);
            if (!recentRoots.Any() && !isExplicitDevMode)
            {
                _logger.LogWarning("[NewReactionContentHandler] No Merkle roots found for membership scope {FeedId}", membershipScopeId.Value);
                return false;
            }

            if (!recentRoots.Any() && isExplicitDevMode)
            {
                recentRoots = new[]
                {
                    new MerkleRootHistory(
                        Id: 0,
                        FeedId: membershipScopeId.Value,
                        MerkleRoot: ZeroBytes32,
                        BlockHeight: 0,
                        CreatedAt: DateTime.UtcNow)
                };
            }

            // Convert payload ciphertexts to ECPoints
            var c1Points = new ECPoint[6];
            var c2Points = new ECPoint[6];
            for (int i = 0; i < 6; i++)
            {
                c1Points[i] = ECPoint.FromCoordinates(payload.CiphertextC1X[i], payload.CiphertextC1Y[i]);
                c2Points[i] = ECPoint.FromCoordinates(payload.CiphertextC2X[i], payload.CiphertextC2Y[i]);
            }

            var rootSnapshots = recentRoots
                .Select(root => new RootDebugSnapshot(root.BlockHeight, ToHex(root.MerkleRoot)))
                .ToArray();

            _logger.LogInformation(
                "[NewReactionContentHandler] Reaction payload snapshot: MessageId={MessageId}, FeedId={FeedId}, MembershipScopeId={MembershipScopeId}, CircuitVersion={CircuitVersion}, NullifierHex={NullifierHex}, AuthorCommitmentHex={AuthorCommitmentHex}, FeedPkX={FeedPkX}, FeedPkY={FeedPkY}, CiphertextC1X={CiphertextC1X}, CiphertextC1Y={CiphertextC1Y}, CiphertextC2X={CiphertextC2X}, CiphertextC2Y={CiphertextC2Y}, RecentRoots={RecentRoots}",
                payload.MessageId,
                payload.FeedId,
                membershipScopeId.Value,
                payload.CircuitVersion,
                ToHex(payload.Nullifier),
                ToHex(authorCommitment),
                feedPk.X.ToString(CultureInfo.InvariantCulture),
                feedPk.Y.ToString(CultureInfo.InvariantCulture),
                string.Join(",", payload.CiphertextC1X.Select(ToHex)),
                string.Join(",", payload.CiphertextC1Y.Select(ToHex)),
                string.Join(",", payload.CiphertextC2X.Select(ToHex)),
                string.Join(",", payload.CiphertextC2Y.Select(ToHex)),
                SerializeForLog(rootSnapshots));

            // Verify against each recent root (grace period)
            foreach (var rootInfo in recentRoots)
            {
                _logger.LogInformation(
                    "[NewReactionContentHandler] Verifying reaction proof inputs: {ProofInputs}",
                    SerializeForLog(new
                    {
                        payload.MessageId,
                        payload.FeedId,
                        MembershipScopeId = membershipScopeId.Value,
                        RootBlockHeight = rootInfo.BlockHeight,
                        NullifierHex = ToHex(payload.Nullifier),
                        AuthorCommitmentHex = ToHex(authorCommitment),
                        MembersRootHex = ToHex(rootInfo.MerkleRoot),
                        FeedPkX = feedPk.X.ToString(CultureInfo.InvariantCulture),
                        FeedPkY = feedPk.Y.ToString(CultureInfo.InvariantCulture),
                        CiphertextC1X = payload.CiphertextC1X.Select(ToHex).ToArray(),
                        CiphertextC1Y = payload.CiphertextC1Y.Select(ToHex).ToArray(),
                        CiphertextC2X = payload.CiphertextC2X.Select(ToHex).ToArray(),
                        CiphertextC2Y = payload.CiphertextC2Y.Select(ToHex).ToArray(),
                    }));

                var publicInputs = new PublicInputs
                {
                    Nullifier = payload.Nullifier,
                    CiphertextC1 = c1Points,
                    CiphertextC2 = c2Points,
                    MessageId = payload.MessageId.Value.ToByteArray(),
                    FeedId = payload.FeedId.Value.ToByteArray(),
                    FeedPk = feedPk,
                    MembersRoot = rootInfo.MerkleRoot,
                    AuthorCommitment = new BigInteger(authorCommitment, isUnsigned: true, isBigEndian: true)
                };

                var verificationStopwatch = Stopwatch.StartNew();
                var verifyResult = await _zkVerifier.VerifyAsync(payload.ZkProof, publicInputs, payload.CircuitVersion);
                verificationStopwatch.Stop();
                if (verifyResult.Valid)
                {
                    _logger.LogInformation(
                        "[NewReactionContentHandler] ZK proof verified for message {MessageId} in {ElapsedMs}ms",
                        payload.MessageId,
                        verificationStopwatch.ElapsedMilliseconds);
                    return true;
                }

                _logger.LogWarning(
                    "[NewReactionContentHandler] Proof candidate rejected for message {MessageId}. CircuitVersion={CircuitVersion}, RootBlockHeight={RootBlockHeight}",
                    payload.MessageId,
                    payload.CircuitVersion,
                    rootInfo.BlockHeight);
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NewReactionContentHandler] Error verifying ZK proof for message {MessageId}", payload.MessageId);
            return false;
        }
    }

    private bool HasPendingReactionForSameTarget(string? signatory, NewReactionPayload payload)
    {
        if (string.IsNullOrWhiteSpace(signatory))
        {
            return false;
        }

        var pendingTransactions = _memPoolService.PeekPendingValidatedTransactions() ?? Enumerable.Empty<AbstractTransaction>();
        return pendingTransactions
            .OfType<ValidatedTransaction<NewReactionPayload>>()
            .Any(x =>
                string.Equals(x.UserSignature?.Signatory, signatory, StringComparison.Ordinal) &&
                Equals(x.Payload.FeedId, payload.FeedId) &&
                Equals(x.Payload.MessageId, payload.MessageId));
    }

    private static bool HasValidCoordinateLengths(NewReactionPayload payload)
    {
        static bool HasExpectedLength(byte[] value) => value.Length == CoordinateLengthBytes;

        return payload.CiphertextC1X.All(HasExpectedLength)
            && payload.CiphertextC1Y.All(HasExpectedLength)
            && payload.CiphertextC2X.All(HasExpectedLength)
            && payload.CiphertextC2Y.All(HasExpectedLength);
    }

    private static bool HasValidBackupLength(byte[]? encryptedEmojiBackup) =>
        encryptedEmojiBackup is { Length: LegacyBackupLengthBytes or VersionedBackupLengthBytes };

    private static bool IsSupportedCircuitVersionFormat(string circuitVersion) =>
        !string.IsNullOrWhiteSpace(circuitVersion) && SupportedCircuitVersionPattern.IsMatch(circuitVersion);

    private static string ToHex(byte[] value) => Convert.ToHexString(value);

    private static string SerializeForLog<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value);
}
