using Grpc.Core;
using HushNetwork;
using HushServerNode.Blockchain;

namespace HushServerNode.Services;

public class HushBlockchainService : HushBlockchain.HushBlockchainBase
{
    private readonly IBlockchainService _blockchainService;

    public HushBlockchainService(IBlockchainService blockchainService)
    {
        this._blockchainService = blockchainService; 
    }

    public override Task<GetBlockchainHeightReply> GetBlockchainHeight(GetBlockchainHeightRequest request, ServerCallContext context)
    {
        return Task.FromResult(new GetBlockchainHeightReply
        {
            Index = this._blockchainService.CurrentBlockIndex
        });
    }
}
