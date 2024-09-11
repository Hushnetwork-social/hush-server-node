using HushEcosystem.Model;
using HushEcosystem.Model.Blockchain;
using HushServerNode.InternalModule.Blockchain.Cache;
using HushServerNode.InternalModule.Blockchain.Events;

namespace HushServerNode.InternalModule.Blockchain.Factories;

public class BlockCreatedEventFactory : IBlockCreatedEventFactory
{
    private readonly TransactionBaseConverter _transactionBaseConverter;

    public BlockCreatedEventFactory(TransactionBaseConverter transactionBaseConverter)
    {
        this._transactionBaseConverter = transactionBaseConverter;
    }

    public BlockCreatedEvent GetInstance(Block block, BlockchainState blockchainState)
    {
        return new BlockCreatedEvent(block, blockchainState, this._transactionBaseConverter);
    }
}