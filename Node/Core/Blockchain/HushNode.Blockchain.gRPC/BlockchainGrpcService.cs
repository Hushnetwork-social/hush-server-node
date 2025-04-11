using System.Text.Json;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.Blockchain.Storage;
using HushNode.MemPool;
using HushShared.Blockchain.TransactionModel;

namespace HushNode.Blockchain.gRPC;

public class BlockchainGrpcService(
    IBlockchainStorageService blockchainStorageService,
    IEnumerable<ITransactionContentHandler> transactionContentHandlers,
    IMemPoolService memPoolService) : HushBlockchain.HushBlockchainBase
{
    private readonly IBlockchainStorageService _blockchainStorageService = blockchainStorageService;
    private readonly IEnumerable<ITransactionContentHandler> _transactionContentHandlers = transactionContentHandlers;
    private readonly IMemPoolService _memPoolService = memPoolService;

    public override async Task<GetBlockchainHeightReply> GetBlockchainHeight(
        GetBlockchainHeightRequest request, 
        ServerCallContext context)
    {
        var blockchainState = await this._blockchainStorageService.RetrieveCurrentBlockchainStateAsync();

        return new GetBlockchainHeightReply
        {
            Index = blockchainState.BlockIndex.Value
        };
    }

    public override Task<SubmitSignedTransactionReply> SubmitSignedTransaction(
        SubmitSignedTransactionRequest request, 
        ServerCallContext context)
    {
        var message = string.Empty;
        var successful = false;

        var transaction = JsonSerializer.Deserialize<AbstractTransaction>(request.SignedTransaction) 
            ?? throw new InvalidDataException("Transaction invalid or without handler");
        
        if (this.ValidateUserSignature(transaction))
        {
            foreach (var item in this._transactionContentHandlers)
            {
                if (item.CanValidate(transaction.PayloadKind))
                {
                    var transactionSignedByValidator = item.ValidateAndSign(transaction);

                    if (transactionSignedByValidator == null)
                    {
                        successful = false;
                        message = "Transaction is invalid and was not added to the MemPool";
                    }
                    else
                    {
                        // add the transaction to the MemPool 
                        this._memPoolService.AddVerifiedTransaction(transactionSignedByValidator);
                    }
                    
                    successful = true;
                    message = "Transaction validated and added to MemPool";
                    break;
                }
            }
        }

        return Task.FromResult(new SubmitSignedTransactionReply 
        {
            Successfull = successful,
            Message = message
        });
    }

    private bool ValidateUserSignature(AbstractTransaction transaction)
    {
        return true;
    }
}
