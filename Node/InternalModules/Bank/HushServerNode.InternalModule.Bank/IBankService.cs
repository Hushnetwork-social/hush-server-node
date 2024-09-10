namespace HushServerNode.InternalModule.Bank;

public interface IBankService
{
    Task UpdateBalanceAsync(string address, double value);

    double GetBalance(string address);
}
