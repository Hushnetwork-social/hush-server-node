using HushShared.Blockchain.TransactionModel.States;
using HushShared.Reactions.Model;

namespace HushNode.Reactions;

/// <summary>
/// Interface for handling reaction transactions when blocks are indexed.
/// </summary>
public interface IReactionTransactionHandler
{
    /// <summary>
    /// Process a validated reaction transaction from a finalized block.
    /// Updates the homomorphic tally and stores the nullifier.
    /// </summary>
    Task HandleReactionTransaction(ValidatedTransaction<NewReactionPayload> validatedTransaction);
}
