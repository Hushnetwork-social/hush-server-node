using System.Collections.Generic;
using HushEcosystem.Model.Blockchain;

namespace HushServerNode.Blockchain;

public class BlockchainDb : IBlockchainDb
{
    private readonly IList<Block> _blockchain = new List<Block>();


    public void AddBlock(Block block)
    {
        this._blockchain.Add(block);
    }
}
