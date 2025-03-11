using HushNode.Blockchain.Model.Transaction;
using HushNode.Blockchain.Model.Transaction.States;
using HushNode.Indexing.Interfaces;

namespace HushNode.Bank;

public class RewardTransactionIndexStrategy(IRewardTransactionHandler rewardTransactionHandler) : IIndexStrategy
{
    private readonly IRewardTransactionHandler _rewardTransactionHandler = rewardTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        transaction.PayloadKind == RewardPayloadHandler.RewardPayloadKind;


    public async Task HandleAsync(AbstractTransaction transaction)
    {
        await _rewardTransactionHandler
            .HandleRewardTransactionAsync((ValidatedTransaction<RewardPayload>)transaction);
    }
}
