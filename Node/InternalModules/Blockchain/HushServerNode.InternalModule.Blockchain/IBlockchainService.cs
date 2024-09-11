using HushServerNode.InternalModule.Blockchain.Cache;

namespace HushServerNode.InternalModule.Blockchain;

public interface IBlockchainService
{
    BlockchainState BlockchainState { get; }

    Task InitializeBlockchainAsync();
}
