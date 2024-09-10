using HushServerNode.Cache.Blockchain;

namespace HushServerNode.InternalModule.Blockchain.Events;

public class BlockchainInitializedEvent 
{ 
    public BlockchainState BlockchainState { get; private set; }

    public BlockchainInitializedEvent(BlockchainState blockchainState)
    {
        this.BlockchainState = blockchainState;
    }
}
