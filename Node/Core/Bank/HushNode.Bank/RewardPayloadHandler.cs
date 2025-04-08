using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel.States;

namespace HushNode.Bank;

public static class RewardPayloadHandler
{
    public static Guid RewardPayloadKind { get; } = Guid.Parse("e054b791-5e99-41aa-b870-a7201bc85ec3");

    public static UnsignedTransaction<RewardPayload> CreateRewardTransaction(string token, string amount) => 
        UnsignedTransactionHandler.CreateNew(
            RewardPayloadKind,
            Timestamp.Current,
            new RewardPayload(token, 9, amount));
}

