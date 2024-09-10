using HushServerNode.Cache.Blockchain;

namespace HushServerNode.InternalModule.Blockchain;

public interface IBlockchainService
{
    Task InitializeBlockchainAsync();
}
