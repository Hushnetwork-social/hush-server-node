using HushShared.Blockchain.TransactionModel;

namespace HushNode.Events;

/// <summary>
/// Event raised when a transaction is received and added to the mempool.
/// Used to resume block production when paused, and for test synchronization.
/// </summary>
public class TransactionReceivedEvent(TransactionId transactionId)
{
    /// <summary>
    /// The ID of the transaction that was received.
    /// </summary>
    public TransactionId TransactionId { get; } = transactionId;
}
