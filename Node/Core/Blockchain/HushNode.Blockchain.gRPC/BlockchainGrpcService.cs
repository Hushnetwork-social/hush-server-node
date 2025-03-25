using System.Text.Json;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.Blockchain.Storage;
using HushNode.MemPool;
using HushShared.Blockchain.TransactionModel;

namespace HushNode.Blockchain.gRPC;

public class BlockchainGrpcService(
    IBlockchainStorageService blockchainStorageService,
    IMemPoolService memPoolService) : HushBlockchain.HushBlockchainBase
{
    private readonly IBlockchainStorageService _blockchainStorageService = blockchainStorageService;
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
        var abstractTransaction = JsonSerializer.Deserialize<AbstractTransaction>(request.SignedTransaction);
        
        return base.SubmitSignedTransaction(request, context);
    }
}
