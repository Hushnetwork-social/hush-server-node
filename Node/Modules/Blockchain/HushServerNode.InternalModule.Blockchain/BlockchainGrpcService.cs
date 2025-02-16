using Grpc.Core;
using HushEcosystem.Model.Blockchain;
using HushNetwork.proto;

namespace HushServerNode.InternalModule.Blockchain;

public class BlockchainGrpcService : HushBlockchain.HushBlockchainBase
{
    private readonly IBlockchainStatus _blockchainStatus;

    public BlockchainGrpcService(IBlockchainStatus blockchainStatus)
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
