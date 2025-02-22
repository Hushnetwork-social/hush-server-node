using HushNode.Blockchain.Persistency.Abstractions.Models.Block;

namespace HushNode.Blockchain.Persistency.Abstractions.Models;

public record BlockchainBlock(
    BlockId BlockId,
    BlockIndex BlockIndex,
    BlockId PreviousBlockId,
    BlockId NextBlockId,
    string Hash, 
    string BlockJson);
