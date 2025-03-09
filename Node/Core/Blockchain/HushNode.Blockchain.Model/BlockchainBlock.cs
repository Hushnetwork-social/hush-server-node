using HushNode.Blockchain.Model.Block;

namespace HushNode.Blockchain.Model;

public record BlockchainBlock(
    BlockId BlockId,
    BlockIndex BlockIndex,
    BlockId PreviousBlockId,
    BlockId NextBlockId,
    string Hash, 
    string BlockJson);
