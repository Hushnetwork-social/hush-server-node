using System.Text.Json;
using Olimpo;
using HushNode.Blockchain.Model.Transaction;

namespace HushNode.Blockchain.Model.Block.States;

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
