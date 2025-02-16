using HushEcosystem.Model.Blockchain;
using HushServerNode.InternalModule.Blockchain.Cache;
using HushServerNode.InternalModule.Blockchain.Events;

namespace HushServerNode.InternalModule.Blockchain.Factories;

public interface IBlockCreatedEventFactory
{
    BlockCreatedEvent GetInstance(Block block, BlockchainState blockchainState);
}
