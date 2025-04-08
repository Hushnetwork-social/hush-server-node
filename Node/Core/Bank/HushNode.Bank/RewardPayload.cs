using HushShared.Blockchain.TransactionModel;

namespace HushNode.Bank;

public record RewardPayload(string Token, int Precision, string Amount) : ITransactionPayloadKind;
