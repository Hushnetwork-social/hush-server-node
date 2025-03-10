using Grpc.Core;
using HushNetwork.proto;
using HushNode.Blockchain.Storage;

namespace HushNode.Blockchain.gRPC;

public class BlockchainGrpcService(IBlockchainStorageService blockchainStorageService) : HushBlockchain.HushBlockchainBase
{
    private readonly IBlockchainStorageService _blockchainStorageService = blockchainStorageService;

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
}
