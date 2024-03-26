using HushEcosystem.Model.Blockchain;

namespace HushServerNode.Blockchain;

public interface IBlockVerifier
{
    bool IsBlockValid(Block block);    
}
