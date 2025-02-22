using System.Text.Json.Serialization;
using HushNetwork.BlockModel;
using HushNetwork.BlockModel.Signed;
using HushNetwork.BlockModel.Unsigned;
using HushNetwork.CommonModel;
using HushNetwork.TransactionModel;

namespace HushNetwork.Shared.Model.Block.Finalized;

public record FinalizedBlock : SignedBlock
{
    public string Hash { get; init; }

    public FinalizedBlock(
        SignedBlock signedBlock, 
        string hash) 
        : base(signedBlock)
    {
        Hash = hash;
    }

    [JsonConstructor]
    public FinalizedBlock(
        BlockId BlockId,
        Timestamp CreationTimeStamp,
        BlockIndex BlockIndex,
        BlockId PreviousBlockId,
        BlockId NextBlockId,
        SignatureInfo BlockProducerSignature,
        string Hash,
        AbstractTransaction[] Transactions) 
        : base(new SignedBlock(
            new UnsignedBlock(
                BlockId,
                CreationTimeStamp,
                BlockIndex,
                PreviousBlockId,
                NextBlockId,
                Transactions), 
            BlockProducerSignature))
    {
        this.Hash = Hash;   
    }
}
