using System.Text.Json;
using HushNetwork.CommonModel;
using HushNetwork.TransactionModel;
using Olimpo;

namespace HushNetwork.BlockModel.Unsigned;

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
