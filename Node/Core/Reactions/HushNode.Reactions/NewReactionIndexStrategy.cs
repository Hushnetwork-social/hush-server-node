using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Reactions.Model;

namespace HushNode.Reactions;

/// <summary>
/// Routes reaction transactions to the handler when blocks are indexed.
/// </summary>
public class NewReactionIndexStrategy : IIndexStrategy
{
    private readonly IReactionTransactionHandler _reactionTransactionHandler;

    public NewReactionIndexStrategy(IReactionTransactionHandler reactionTransactionHandler)
    {
        _reactionTransactionHandler = reactionTransactionHandler;
    }

    public bool CanHandle(AbstractTransaction transaction)
    {
        var canHandle = NewReactionPayloadHandler.NewReactionPayloadKind == transaction.PayloadKind;
        if (canHandle)
        {
            Console.WriteLine($"[E2E Reaction] NewReactionIndexStrategy.CanHandle: TRUE - PayloadKind matches");
        }
        return canHandle;
    }

    public async Task HandleAsync(AbstractTransaction transaction)
    {
        Console.WriteLine($"[E2E Reaction] NewReactionIndexStrategy.HandleAsync: Processing reaction transaction {transaction.TransactionId}");
        await _reactionTransactionHandler.HandleReactionTransaction(
            (ValidatedTransaction<NewReactionPayload>)transaction);
        Console.WriteLine($"[E2E Reaction] NewReactionIndexStrategy.HandleAsync: Completed processing transaction {transaction.TransactionId}");
    }
}
