using System.Text.Json;
using HushNode.Blockchain.Persistency.Abstractions.Models.Transaction;
using Olimpo;

namespace HushNode.Blockchain.Persistency.Abstractions.Models.Block.States;

public record UnsignedBlock(
    BlockId BlockId,
    Timestamp CreationTimeStamp,
    BlockIndex BlockIndex,
    BlockId PreviousBlockId,
    BlockId NextBlockId,
    AbstractTransaction[] Transactions)
{
    public string ToJson() => 
        JsonSerializer.Serialize(this);

    public string CreateSignature(string privateKey) => 
        DigitalSignature.SignMessage(ToJson(), privateKey);
}
