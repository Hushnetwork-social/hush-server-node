using HushNode.Blockchain.Persistency.Abstractions.Models;
using HushNode.Blockchain.Persistency.Abstractions.Models.Transaction;
using HushNode.Blockchain.Persistency.Abstractions.Models.Transaction.States;

namespace HushNode.InternalPayloads
{
    public record RewardPayload(string Token, int Precision, string Amount) : ITransactionPayloadKind;

    public static class RewardPayloadHandler
    {
        public static Guid RewardPayloadKind { get; } = Guid.Parse("e054b791-5e99-41aa-b870-a7201bc85ec3");

        public static UnsignedTransaction<RewardPayload> CreateRewardTransaction(string token, string amount) => 
            UnsignedTransactionHandler.CreateNew(
                RewardPayloadKind,
                Timestamp.Current,
                new RewardPayload(token, 9, amount));
    }
}
