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

    public bool CanHandle(AbstractTransaction transaction) =>
        NewReactionPayloadHandler.NewReactionPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await _reactionTransactionHandler.HandleReactionTransaction(
            (ValidatedTransaction<NewReactionPayload>)transaction);
}
