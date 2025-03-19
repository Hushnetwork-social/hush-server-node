using HushNode.Blockchain.Model.Block;

namespace HushNode.Blockchain.Storage.Model;

public record BlockchainBlock(
    BlockId BlockId,
    BlockIndex BlockIndex,
    BlockId PreviousBlockId,
    BlockId NextBlockId,
    string Hash, 
    string BlockJson);
