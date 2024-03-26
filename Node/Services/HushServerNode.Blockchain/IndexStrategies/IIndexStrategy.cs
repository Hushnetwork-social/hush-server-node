using System.Threading.Tasks;
using HushEcosystem.Model.Blockchain;

namespace HushServerNode.Blockchain.IndexStrategies;

public interface IIndexStrategy
{
    bool CanHandle(VerifiedTransaction verifiedTransaction);

    Task Handle(VerifiedTransaction verifiedTransaction);    
}
