using System.Collections.Generic;
using System.Threading.Tasks;
using HushEcosystem.Model.Blockchain;

namespace HushServerNode.Blockchain;

public interface IBlockchainService
{
    string CurrentBlockId { get; }
    double CurrentBlockIndex { get; }
    string CurrentNextBlockId { get; }
    string CurrentPreviousBlockId { get; }

    Task InitializeBlockchainAsync();

    IEnumerable<VerifiedTransaction> ListTransactionsForAddress(string address, double lastHeightSynched);

    double GetBalanceForAddress(string address);

    UserProfile GetUserProfile(string publicAddress);
}
