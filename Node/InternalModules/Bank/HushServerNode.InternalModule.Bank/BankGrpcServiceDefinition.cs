using Grpc.Core;
using HushNetwork.proto;
using HushServerNode.Interfaces;

namespace HushServerNode.InternalModule.Bank;

public class BankGrpcServiceDefinition : IGrpcDefinition
{
    private readonly HushBank.HushBankBase _bankGrpcService;

    public BankGrpcServiceDefinition(HushBank.HushBankBase bankGrpcService)
    {
        this._bankGrpcService = bankGrpcService;
    }

    public void AddGrpcService(Server server)
    {
        server.Services.Add(HushBank.BindService(this._bankGrpcService));
    }
}
