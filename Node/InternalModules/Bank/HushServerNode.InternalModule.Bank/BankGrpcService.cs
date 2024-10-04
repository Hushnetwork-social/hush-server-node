using Grpc.Core;
using HushEcosystem;
using HushNetwork.proto;
using HushServerNode.InternalModule.MemPool.Events;
using Olimpo;

namespace HushServerNode.InternalModule.Bank;

public class BankGrpcService : HushBank.HushBankBase
{
    private readonly IBankService _bankService;
    private readonly IEventAggregator _eventAggregator;

    public BankGrpcService(
        IBankService bankService,
        IEventAggregator eventAggregator)
    {
        this._bankService = bankService;
        this._eventAggregator = eventAggregator;
    }

    public override Task<GetAddressBalanceReply> GetAddressBalance(GetAddressBalanceRequest request, ServerCallContext context)
    {
        var balance = this._bankService.GetBalance(request.Address);

        return Task.FromResult(new GetAddressBalanceReply
        {
            Balance = balance.DoubleToString()
        });
    }

    public override async Task<TransferFundsReply> TransferFunds(TransferFundsRequest request, ServerCallContext context)
    {
        // TODO [AboimPinto]: Need to check if the funds transfer is valid or not
        // this._bankService.CheckIfTransferIsValid()

        await this._eventAggregator.PublishAsync(new AddTrasactionToMemPoolEvent(
            new HushEcosystem.Model.Bank.TransferFunds
            {
                TransferFundsId = request.Id,
                FeedId = request.FeedId,
                Issuer = request.FromAddress,
                Token = request.Token,
                Value = request.Amount.StringToDouble(),
                ReceiverPublicAddress = request.ToAddress,
                Hash = request.Hash,
                Signature = request.Signature,
                FeedPublicEncriptAddress = request.FeedPublicEncriptAddress,
            }
        ));

        return new TransferFundsReply()
        {
            Successfull = true,
            Message = string.Empty,
        };
    }
}
