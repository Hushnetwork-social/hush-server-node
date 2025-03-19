using HushShared.Blockchain.TransactionModel.States;

namespace HushNode.Bank;

public class RewardTransactionReceivedEvent(ValidatedTransaction<RewardPayload> rewardTransaction)
{
    public ValidatedTransaction<RewardPayload> RewardTransaction { get; } = rewardTransaction;
}
