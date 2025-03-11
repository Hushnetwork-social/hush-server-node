using HushNode.Blockchain.Model.Transaction.States;

namespace HushNode.Bank;

public interface IRewardTransactionHandler
{
    Task HandleRewardTransactionAsync(ValidatedTransaction<RewardPayload> rewardTransaction);
}
