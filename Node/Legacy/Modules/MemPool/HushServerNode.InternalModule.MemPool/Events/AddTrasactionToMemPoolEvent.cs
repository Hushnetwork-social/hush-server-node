using HushEcosystem.Model;

namespace HushServerNode.InternalModule.MemPool.Events;

public class AddTrasactionToMemPoolEvent
{
    public TransactionBase Transaction { get; private set; }

    public AddTrasactionToMemPoolEvent(TransactionBase transaction)
    {
        this.Transaction = transaction;
    }
}
