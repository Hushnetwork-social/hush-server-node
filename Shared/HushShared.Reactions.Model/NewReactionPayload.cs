using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushShared.Reactions.Model;

/// <summary>
/// Blockchain transaction payload for anonymous reactions (Protocol Omega).
/// Contains all data needed for blockchain replay.
/// </summary>
public record NewReactionPayload(
    FeedId FeedId,
    FeedMessageId MessageId,
    byte[] Nullifier,           // 32 bytes - deterministic for (user, message, feed)
    byte[][] CiphertextC1X,     // 6 x 32 bytes - ElGamal C1.X for each emoji
    byte[][] CiphertextC1Y,     // 6 x 32 bytes - ElGamal C1.Y for each emoji
    byte[][] CiphertextC2X,     // 6 x 32 bytes - ElGamal C2.X for each emoji
    byte[][] CiphertextC2Y,     // 6 x 32 bytes - ElGamal C2.Y for each emoji
    byte[] ZkProof,             // ~256 bytes - Groth16 proof
    string CircuitVersion,      // e.g., "omega-v1.0.0" or "dev-mode-v1"
    byte[]? EncryptedEmojiBackup // Optional backup for cross-device recovery
) : ITransactionPayloadKind;

public static class NewReactionPayloadHandler
{
    /// <summary>
    /// Unique identifier for this transaction type.
    /// </summary>
    public static Guid NewReactionPayloadKind { get; } = Guid.Parse("a7b3c2d1-e4f5-6789-abcd-ef0123456789");

    /// <summary>
    /// Creates an unsigned reaction transaction.
    /// </summary>
    public static UnsignedTransaction<NewReactionPayload> CreateNewReactionTransaction(
        FeedId feedId,
        FeedMessageId messageId,
        byte[] nullifier,
        byte[][] ciphertextC1X,
        byte[][] ciphertextC1Y,
        byte[][] ciphertextC2X,
        byte[][] ciphertextC2Y,
        byte[] zkProof,
        string circuitVersion,
        byte[]? encryptedEmojiBackup = null) =>
        UnsignedTransactionHandler.CreateNew(
            NewReactionPayloadKind,
            Timestamp.Current,
            new NewReactionPayload(
                feedId,
                messageId,
                nullifier,
                ciphertextC1X,
                ciphertextC1Y,
                ciphertextC2X,
                ciphertextC2Y,
                zkProof,
                circuitVersion,
                encryptedEmojiBackup));
}
