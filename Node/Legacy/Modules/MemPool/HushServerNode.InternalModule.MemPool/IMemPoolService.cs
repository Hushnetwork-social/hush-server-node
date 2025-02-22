using HushEcosystem.Model.Blockchain;

namespace HushServerNode.InternalModule.MemPool;

public interface IMemPoolService
{
    Task InitializeMemPool();

    IEnumerable<VerifiedTransaction> GetNextBlockTransactionsCandidate();
}
