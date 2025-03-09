using HushNode.Blockchain.Model.Transaction.States;
using HushNode.InternalPayloads;

namespace HushNode.InternalModules.Bank;

public class RewardTransactionReceivedEvent(ValidatedTransaction<RewardPayload> rewardTransaction)
{
    public ValidatedTransaction<RewardPayload> RewardTransaction { get; } = rewardTransaction;
}
