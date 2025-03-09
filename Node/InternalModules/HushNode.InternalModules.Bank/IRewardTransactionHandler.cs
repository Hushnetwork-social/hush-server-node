using HushNode.Blockchain.Model.Transaction.States;
using HushNode.InternalPayloads;

namespace HushNode.InternalModules.Bank;

public interface IRewardTransactionHandler
{
    Task HandleRewardTransactionAsync(ValidatedTransaction<RewardPayload> rewardTransaction);
}
