using System.Collections.Generic;
using HushEcosystem.Model.Blockchain;

namespace HushServerNode.Blockchain;

public interface IBlockchainDb
{
    // List<Block> Blockchain { get; }

    void AddBlock(Block block);
}
