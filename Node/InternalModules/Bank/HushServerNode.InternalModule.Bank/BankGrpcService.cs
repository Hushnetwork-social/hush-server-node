using Grpc.Core;
using HushEcosystem;
using HushNetwork.proto;

namespace HushServerNode.InternalModule.Bank;

public class BankGrpcService : HushBank.HushBankBase
{
    private readonly IBankService _bankService;

    public BankGrpcService(IBankService bankService)
    {
        this._bankService = bankService;
    }

    public override Task<GetAddressBalanceReply> GetAddressBalance(GetAddressBalanceRequest request, ServerCallContext context)
    {
        var balance = this._bankService.GetBalance(request.Address);

        return Task.FromResult(new GetAddressBalanceReply
        {
            Balance = balance.DoubleToString()
        });
    }
}
