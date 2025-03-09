using Grpc.Core;
using HushNetwork.proto;
using HushNode.Blockchain.Repositories;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Blockchain.gRPC;

public class BlockchainGrpcService(IUnitOfWorkProvider<BlockchainDbContext> unitOfWorkProvider) : HushBlockchain.HushBlockchainBase
{
    private readonly IUnitOfWorkProvider<BlockchainDbContext> _unitOfWorkProvider = unitOfWorkProvider;

    public override async Task<GetBlockchainHeightReply> GetBlockchainHeight(
        GetBlockchainHeightRequest request, 
        ServerCallContext context)
    {
        using var readableUnitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var blockchainState = await readableUnitOfWork
            .GetRepository<IBlockchainStateRepository>()
            .GetCurrentStateAsync();

        return new GetBlockchainHeightReply
        {
            Index = blockchainState.BlockIndex.Value
        };
    }
}
