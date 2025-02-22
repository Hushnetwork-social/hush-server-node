using HushEcosystem.Model.Blockchain;

namespace HushServerNode.InternalModule.Blockchain;

public interface IBlockVerifier
{
    bool IsBlockValid(Block block);    
}
