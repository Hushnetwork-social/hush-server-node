using HushNode.Blockchain.Persistency.Abstractions.Models.Transaction;
using HushNode.Blockchain.Persistency.Abstractions.Models.Transaction.States;
using HushNode.Indexing;
using HushNode.InternalPayloads;

namespace HushNode.InternalModules.Bank;

public class RewardTransactionIndexStrategy(IRewardTransactionHandler rewardTransactionHandler) : IIndexStrategy
{
    private readonly IRewardTransactionHandler _rewardTransactionHandler = rewardTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        transaction.PayloadKind == RewardPayloadHandler.RewardPayloadKind;


    public async Task HandleAsync(AbstractTransaction transaction)
    {
        await this._rewardTransactionHandler
            .HandleRewardTransactionAsync((ValidatedTransaction<RewardPayload>)transaction);
    }
}
