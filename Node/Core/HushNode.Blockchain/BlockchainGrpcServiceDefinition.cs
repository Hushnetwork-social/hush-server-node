using Grpc.Core;
using HushNetwork.proto;
using HushNode.Interfaces;

namespace HushNode.Blockchain;

public class BlockchainGrpcServiceDefinition : IGrpcDefinition
{
    private readonly HushBlockchain.HushBlockchainBase _blockchainGrpcService;

    public BlockchainGrpcServiceDefinition(HushBlockchain.HushBlockchainBase blockchainGrpcService)
    {
        this._blockchainGrpcService = blockchainGrpcService;
    }

    public void AddGrpcService(Server server)
    {
        server.Services.Add(HushBlockchain.BindService(this._blockchainGrpcService));
    }
}
