using System.Text.Json.Serialization;
using HushNetwork.Shared.Model.Block;
using HushServerNode.InternalModule.Blockchain.Cache.Converters;

namespace HushServerNode.InternalModule.Blockchain.Cache;

// public class BlockchainState
// {
//     public Guid BlockchainStateId { get; set; }

//     public long LastBlockIndex { get; set; }    

//     public string CurrentBlockId { get; set; } = string.Empty;

//     public string CurrentPreviousBlockId { get; set; } = string.Empty;

//     public string CurrentNextBlockId { get; set; } = string.Empty;
// }

public record BlockchainState(
    BlockchainStateId BlockchainStateId,
    BlockIndex LastBlockIndex,
    BlockId CurrentBlockId,
    BlockId PreviousBlockId,
    BlockId NextBlockId)
{

    public static BlockchainState CreateGenesisBlockchainState() => 
        new(
            BlockchainStateId.NewBlockchainStateId,
            BlockIndexHandler.CreateNew(1),
            BlockId.Empty,
            BlockId.NewBlockId,
            BlockId.NewBlockId);
}

[JsonConverter(typeof(BlockchainStateIdConverter))]
public readonly record struct BlockchainStateId(Guid Value)
{
    public static BlockchainStateId Empty { get; } = new(Guid.Empty);
    public static BlockchainStateId NewBlockchainStateId { get; } = new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}