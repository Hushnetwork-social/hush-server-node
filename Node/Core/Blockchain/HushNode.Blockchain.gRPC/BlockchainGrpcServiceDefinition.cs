using Grpc.Core;
using HushNetwork.proto;
using HushNode.Interfaces;

namespace HushNode.Blockchain.gRPC;

public class BlockchainGrpcServiceDefinition(HushBlockchain.HushBlockchainBase blockchainGrpcService) : IGrpcDefinition
{
    private readonly HushBlockchain.HushBlockchainBase _blockchainGrpcService = blockchainGrpcService;

    public void AddGrpcService(Server server)
    {
        server.Services.Add(HushBlockchain.BindService(this._blockchainGrpcService));
    }
}
