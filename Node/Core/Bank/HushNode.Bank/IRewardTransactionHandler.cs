using HushShared.Blockchain.TransactionModel.States;

namespace HushNode.Bank;

public interface IRewardTransactionHandler
{
    Task HandleRewardTransactionAsync(ValidatedTransaction<RewardPayload> rewardTransaction);
}
