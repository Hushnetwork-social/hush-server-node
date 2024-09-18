using Grpc.Core;
using HushEcosystem.Model.Blockchain;
using HushNetwork.proto;

namespace HushServerNode.Services;

public class HushBlockchainService : HushBlockchain.HushBlockchainBase
{
    private readonly IBlockchainStatus _blockchainStatus;

    public HushBlockchainService(IBlockchainStatus blockchainStatus)
    {
        this._blockchainStatus = blockchainStatus;
    }

    public override Task<GetBlockchainHeightReply> GetBlockchainHeight(GetBlockchainHeightRequest request, ServerCallContext context)
    {
        return Task.FromResult(new GetBlockchainHeightReply
        {
            Index = this._blockchainStatus.BlockIndex
        });
    }
}
