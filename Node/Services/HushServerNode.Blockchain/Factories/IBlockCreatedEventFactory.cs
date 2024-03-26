using HushEcosystem.Model.Blockchain;
using HushServerNode.Blockchain.Events;

namespace HushServerNode.Blockchain.Factories;

public interface IBlockCreatedEventFactory
{
    BlockCreatedEvent GetInstance(Block block);
}
