using Microsoft.Extensions.Logging;
using Olimpo.EntityFramework.Persistency;
using HushNode.Caching;
using HushNode.Reactions.Crypto;
using HushNode.Reactions.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using HushShared.Reactions.Model;

namespace HushNode.Reactions;

/// <summary>
/// Handles reaction transactions when blocks are indexed.
/// Processes the homomorphic tally updates and stores nullifiers.
///
/// Note: ZK proof validation happens during mempool entry (in NewReactionContentHandler).
/// This handler assumes the transaction is already validated.
/// </summary>
public class ReactionTransactionHandler : IReactionTransactionHandler
{
    private readonly IUnitOfWorkProvider<ReactionsDbContext> _unitOfWorkProvider;
    private readonly IBabyJubJub _curve;
    private readonly IBlockchainCache _blockchainCache;
    private readonly ILogger<ReactionTransactionHandler> _logger;

    public ReactionTransactionHandler(
        IUnitOfWorkProvider<ReactionsDbContext> unitOfWorkProvider,
        IBabyJubJub curve,
        IBlockchainCache blockchainCache,
        ILogger<ReactionTransactionHandler> logger)
    {
        _unitOfWorkProvider = unitOfWorkProvider;
        _curve = curve;
        _blockchainCache = blockchainCache;
        _logger = logger;
    }

    public async Task HandleReactionTransaction(ValidatedTransaction<NewReactionPayload> validatedTransaction)
    {
        var payload = validatedTransaction.Payload;
        var issuerPublicAddress = validatedTransaction.UserSignature?.Signatory ?? string.Empty;

        _logger.LogInformation(
            "[ReactionTransactionHandler] Processing reaction for message {MessageId} from {Issuer}",
            payload.MessageId,
            issuerPublicAddress.Length > 20 ? issuerPublicAddress[..20] + "..." : issuerPublicAddress);

        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            // Use serializable transaction for atomic tally update
            using var unitOfWork = _unitOfWorkProvider.CreateWritable(System.Data.IsolationLevel.Serializable);
            var reactionsRepo = unitOfWork.GetRepository<IReactionsRepository>();

            try
            {
                // Get existing tally (with row-level lock)
                var tally = await reactionsRepo.GetTallyForUpdateAsync(payload.MessageId);

                if (tally == null)
                {
                    // Initialize new tally with identity points
                    tally = CreateEmptyTally(payload.FeedId, payload.MessageId);
                }

                // Check if this is an update (nullifier already exists)
                var existingNullifier = await reactionsRepo.GetNullifierAsync(payload.Nullifier);
                var isUpdate = existingNullifier != null;

                // Build new vote arrays from payload
                var newVote = new VoteArrays
                {
                    C1X = payload.CiphertextC1X,
                    C1Y = payload.CiphertextC1Y,
                    C2X = payload.CiphertextC2X,
                    C2Y = payload.CiphertextC2Y
                };

                // Update tally with homomorphic operations
                if (isUpdate)
                {
                    // Subtract old vote, add new vote
                    var oldVote = ParseNullifierVote(existingNullifier!);
                    tally = UpdateTallyWithDelta(tally, oldVote, newVote);

                    // Update the nullifier record
                    var updatedNullifier = existingNullifier! with
                    {
                        VoteC1X = newVote.C1X,
                        VoteC1Y = newVote.C1Y,
                        VoteC2X = newVote.C2X,
                        VoteC2Y = newVote.C2Y,
                        EncryptedEmojiBackup = payload.EncryptedEmojiBackup,
                        UpdatedAt = DateTime.UtcNow
                    };
                    await reactionsRepo.UpdateNullifierAsync(updatedNullifier);

                    _logger.LogDebug("[ReactionTransactionHandler] Updated existing reaction for message {MessageId}", payload.MessageId);
                }
                else
                {
                    // Add new vote to tally
                    tally = AddVoteToTally(tally, newVote);

                    // Create new nullifier record
                    var newNullifier = new ReactionNullifier(
                        Nullifier: payload.Nullifier,
                        MessageId: payload.MessageId,
                        VoteC1X: newVote.C1X,
                        VoteC1Y: newVote.C1Y,
                        VoteC2X: newVote.C2X,
                        VoteC2Y: newVote.C2Y,
                        EncryptedEmojiBackup: payload.EncryptedEmojiBackup,
                        CreatedAt: DateTime.UtcNow,
                        UpdatedAt: DateTime.UtcNow);
                    await reactionsRepo.SaveNullifierAsync(newNullifier);

                    // Increment total count for new reactions
                    tally = tally with { TotalCount = tally.TotalCount + 1 };

                    _logger.LogDebug("[ReactionTransactionHandler] Added new reaction for message {MessageId}", payload.MessageId);
                }

                // Update tally timestamp and increment version for sync
                tally = tally with
                {
                    LastUpdated = DateTime.UtcNow,
                    Version = tally.Version + 1
                };

                // Save tally
                await reactionsRepo.SaveTallyAsync(tally);

                // Save transaction record for blockchain replay audit
                var transaction = new ReactionTransaction(
                    Id: validatedTransaction.TransactionId.Value,
                    BlockHeight: _blockchainCache.LastBlockIndex,
                    FeedId: payload.FeedId,
                    MessageId: payload.MessageId,
                    Nullifier: payload.Nullifier,
                    CiphertextC1X: payload.CiphertextC1X,
                    CiphertextC1Y: payload.CiphertextC1Y,
                    CiphertextC2X: payload.CiphertextC2X,
                    CiphertextC2Y: payload.CiphertextC2Y,
                    ZkProof: payload.ZkProof,
                    CircuitVersion: payload.CircuitVersion,
                    CreatedAt: DateTime.UtcNow);
                await reactionsRepo.SaveTransactionAsync(transaction);

                await unitOfWork.CommitAsync();

                _logger.LogInformation(
                    "[ReactionTransactionHandler] Reaction processed successfully: message={MessageId}, isUpdate={IsUpdate}, block={BlockIndex}",
                    payload.MessageId, isUpdate, _blockchainCache.LastBlockIndex);

                return; // Success - exit the retry loop
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "40001") // Serialization failure
            {
                if (attempt == maxRetries)
                {
                    _logger.LogError(ex, "[ReactionTransactionHandler] Serialization failure after {MaxRetries} attempts for message {MessageId}", maxRetries, payload.MessageId);
                    throw;
                }

                _logger.LogWarning("[ReactionTransactionHandler] Serialization conflict, retrying ({Attempt}/{MaxRetries}) for message {MessageId}", attempt, maxRetries, payload.MessageId);
                await Task.Delay(50 * attempt); // Exponential backoff
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactionTransactionHandler] Failed to process reaction for message {MessageId}", payload.MessageId);
                throw;
            }
        }
    }

    private MessageReactionTally CreateEmptyTally(FeedId feedId, FeedMessageId messageId)
    {
        // Initialize all points to identity (0, 1)
        var identityX = _curve.Identity.X.ToByteArray(isUnsigned: true, isBigEndian: true);
        var identityY = _curve.Identity.Y.ToByteArray(isUnsigned: true, isBigEndian: true);

        // Pad to 32 bytes
        identityX = PadTo32Bytes(identityX);
        identityY = PadTo32Bytes(identityY);

        return new MessageReactionTally(
            MessageId: messageId,
            FeedId: feedId,
            TallyC1X: Enumerable.Repeat(identityX, 6).ToArray(),
            TallyC1Y: Enumerable.Repeat(identityY, 6).ToArray(),
            TallyC2X: Enumerable.Repeat(identityX, 6).ToArray(),
            TallyC2Y: Enumerable.Repeat(identityY, 6).ToArray(),
            TotalCount: 0,
            Version: 1,
            LastUpdated: DateTime.UtcNow);
    }

    private VoteArrays ParseNullifierVote(ReactionNullifier nullifier)
    {
        return new VoteArrays
        {
            C1X = nullifier.VoteC1X,
            C1Y = nullifier.VoteC1Y,
            C2X = nullifier.VoteC2X,
            C2Y = nullifier.VoteC2Y
        };
    }

    private MessageReactionTally UpdateTallyWithDelta(
        MessageReactionTally tally,
        VoteArrays oldVote,
        VoteArrays newVote)
    {
        var newTallyC1X = new byte[6][];
        var newTallyC1Y = new byte[6][];
        var newTallyC2X = new byte[6][];
        var newTallyC2Y = new byte[6][];

        for (int i = 0; i < 6; i++)
        {
            // Parse current tally point
            var tallyC1 = ECPoint.FromCoordinates(tally.TallyC1X[i], tally.TallyC1Y[i]);
            var tallyC2 = ECPoint.FromCoordinates(tally.TallyC2X[i], tally.TallyC2Y[i]);

            // Parse old vote point
            var oldC1 = ECPoint.FromCoordinates(oldVote.C1X[i], oldVote.C1Y[i]);
            var oldC2 = ECPoint.FromCoordinates(oldVote.C2X[i], oldVote.C2Y[i]);

            // Parse new vote point
            var newC1 = ECPoint.FromCoordinates(newVote.C1X[i], newVote.C1Y[i]);
            var newC2 = ECPoint.FromCoordinates(newVote.C2X[i], newVote.C2Y[i]);

            // Subtract old, add new: tally = tally - old + new
            var resultC1 = _curve.Add(_curve.Subtract(tallyC1, oldC1), newC1);
            var resultC2 = _curve.Add(_curve.Subtract(tallyC2, oldC2), newC2);

            newTallyC1X[i] = PadTo32Bytes(resultC1.X.ToByteArray(isUnsigned: true, isBigEndian: true));
            newTallyC1Y[i] = PadTo32Bytes(resultC1.Y.ToByteArray(isUnsigned: true, isBigEndian: true));
            newTallyC2X[i] = PadTo32Bytes(resultC2.X.ToByteArray(isUnsigned: true, isBigEndian: true));
            newTallyC2Y[i] = PadTo32Bytes(resultC2.Y.ToByteArray(isUnsigned: true, isBigEndian: true));
        }

        return tally with
        {
            TallyC1X = newTallyC1X,
            TallyC1Y = newTallyC1Y,
            TallyC2X = newTallyC2X,
            TallyC2Y = newTallyC2Y
        };
    }

    private MessageReactionTally AddVoteToTally(MessageReactionTally tally, VoteArrays newVote)
    {
        var newTallyC1X = new byte[6][];
        var newTallyC1Y = new byte[6][];
        var newTallyC2X = new byte[6][];
        var newTallyC2Y = new byte[6][];

        for (int i = 0; i < 6; i++)
        {
            // Parse current tally point
            var tallyC1 = ECPoint.FromCoordinates(tally.TallyC1X[i], tally.TallyC1Y[i]);
            var tallyC2 = ECPoint.FromCoordinates(tally.TallyC2X[i], tally.TallyC2Y[i]);

            // Parse new vote point
            var newC1 = ECPoint.FromCoordinates(newVote.C1X[i], newVote.C1Y[i]);
            var newC2 = ECPoint.FromCoordinates(newVote.C2X[i], newVote.C2Y[i]);

            // Add new vote: tally = tally + new
            var resultC1 = _curve.Add(tallyC1, newC1);
            var resultC2 = _curve.Add(tallyC2, newC2);

            newTallyC1X[i] = PadTo32Bytes(resultC1.X.ToByteArray(isUnsigned: true, isBigEndian: true));
            newTallyC1Y[i] = PadTo32Bytes(resultC1.Y.ToByteArray(isUnsigned: true, isBigEndian: true));
            newTallyC2X[i] = PadTo32Bytes(resultC2.X.ToByteArray(isUnsigned: true, isBigEndian: true));
            newTallyC2Y[i] = PadTo32Bytes(resultC2.Y.ToByteArray(isUnsigned: true, isBigEndian: true));
        }

        return tally with
        {
            TallyC1X = newTallyC1X,
            TallyC1Y = newTallyC1Y,
            TallyC2X = newTallyC2X,
            TallyC2Y = newTallyC2Y
        };
    }

    private static byte[] PadTo32Bytes(byte[] input)
    {
        if (input.Length >= 32)
            return input[..32];

        var result = new byte[32];
        Array.Copy(input, 0, result, 32 - input.Length, input.Length);
        return result;
    }

    private class VoteArrays
    {
        public required byte[][] C1X { get; set; }
        public required byte[][] C1Y { get; set; }
        public required byte[][] C2X { get; set; }
        public required byte[][] C2Y { get; set; }
    }
}
