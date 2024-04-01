using HushEcosystem.Model.Blockchain;

namespace HushServerNode.Blockchain.Events;

public class AddTrasactionToMemPoolEvent
{
    public TransactionBase Transaction { get; set; }    
}
