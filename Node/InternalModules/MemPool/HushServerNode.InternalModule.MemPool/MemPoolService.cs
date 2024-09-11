using System.Collections.Concurrent;
using HushEcosystem.Model;
using HushEcosystem.Model.Blockchain;
using HushServerNode.InternalModule.Blockchain;
using HushServerNode.InternalModule.MemPool.Events;
using Microsoft.Extensions.Configuration;
using Olimpo;

namespace HushServerNode.InternalModule.MemPool;

public class MemPoolService : 
    IMemPoolService,
    IHandle<AddTrasactionToMemPoolEvent>
{
    private ConcurrentBag<VerifiedTransaction> _nextBlockTransactionsCandidate;
    private readonly IConfiguration _configuration;
    private readonly IBlockchainService _blockchainService;
    private readonly TransactionBaseConverter _transactionBaseConverter;

    public MemPoolService(
        IConfiguration configuration,
        IBlockchainService blockchainService,
        TransactionBaseConverter transactionBaseConverter,
        IEventAggregator eventAggregator)
    {
        this._configuration = configuration;
        this._blockchainService = blockchainService;
        this._transactionBaseConverter = transactionBaseConverter;

        this._nextBlockTransactionsCandidate = new ConcurrentBag<VerifiedTransaction>();

        eventAggregator.Subscribe(this);
    }

    public Task InitializeMemPool()
    {        
        // TOOD [AboimPinto]: In case of beeing part of an established network, the mempool should be initialized with the transactions from the other nodes.
        return Task.CompletedTask;
    }

    public IEnumerable<VerifiedTransaction> GetNextBlockTransactionsCandidate()
    {
        // TODO [AboimPinto]: Nee to clarify how many transactions will be added to each block. The number 1000 is just arbitrary and more tests are needed.
        return this._nextBlockTransactionsCandidate.TakeAndRemove(1000);
    }

    public void Handle(AddTrasactionToMemPoolEvent message)
    {
        // TODO [AboimPinto]: Need to check if the transaction is valid before adding it to the mempool.
        // TODO [AboimPinto]: The process is not right. It's adding the BlockIndex and signing the transaction without knowing if the transaction will be in the block with the BlockIndex.
        //                    At this point should only check if the transaction is valid from the application point of the view (i.e. Check if has enough balance to make the transaction or not sending a message to a person that blocked the contact).
        //                    For the MVP this is ok, but, for need to be refactor ASAP.

        var verifiedTransaction = new VerifiedTransaction
        {
            SpecificTransaction = message.Transaction,
            ValidatorAddress = this._configuration["StackerInfo:PublicSigningAddress"],
            BlockIndex = this._blockchainService.BlockchainState.LastBlockIndex
        };
        
        verifiedTransaction.HashObject(this._transactionBaseConverter);
        verifiedTransaction.Sign(this._configuration["StackerInfo:PrivateSigningKey"], this._transactionBaseConverter);

        this._nextBlockTransactionsCandidate.Add(verifiedTransaction);
    }
}
