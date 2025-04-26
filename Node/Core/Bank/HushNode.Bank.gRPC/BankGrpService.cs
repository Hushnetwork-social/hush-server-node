using Grpc.Core;
using HushNetwork.proto;
using HushNode.Bank.Storage;

namespace HushNode.Bank.gRPC;

public class BankGrpService(IBankStorageService bankStorageService) : HushBank.HushBankBase
{
    private readonly IBankStorageService _bankStorageService = bankStorageService;

    public override async Task<GetAddressBalanceReply> GetAddressBalance(GetAddressBalanceRequest request, ServerCallContext context)
    {
        var result = await this._bankStorageService.RetrieveTokenBalanceForAddress(
            request.Address,
            request.Token
        );

        return new GetAddressBalanceReply
        {
            Balance = result.Balance
        };
    }
}
