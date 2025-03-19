using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

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
