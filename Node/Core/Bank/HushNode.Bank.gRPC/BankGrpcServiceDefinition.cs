using Grpc.Core;
using HushNetwork.proto;
using HushNode.Interfaces;

namespace HushNode.Bank.gRPC;

public class BankGrpcServiceDefinition(HushBank.HushBankBase hushBankGrpcService) : IGrpcDefinition
{
    private readonly HushBank.HushBankBase _hushBankGrpcService = hushBankGrpcService;

    public void AddGrpcService(Server server)
    {
        server.Services.Add(HushBank.BindService(this._hushBankGrpcService));
    }
}
