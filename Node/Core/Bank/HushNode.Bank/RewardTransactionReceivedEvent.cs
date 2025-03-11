using HushNode.Blockchain.Model.Transaction.States;

namespace HushNode.Bank;

public class RewardTransactionReceivedEvent(ValidatedTransaction<RewardPayload> rewardTransaction)
{
    public ValidatedTransaction<RewardPayload> RewardTransaction { get; } = rewardTransaction;
}
