using System.Collections.Generic;
using HushEcosystem.Model.Blockchain;
using HushEcosystem.Model.Rpc.Feeds;

namespace HushServerNode.Blockchain;

public interface IBlockchainIndexDb
{
    IDictionary<string, double> AddressBalance { get; set; }

    IDictionary<string, List<VerifiedTransaction>> GroupedTransactions { get; set; }

    IList<UserProfile> Profiles { get; set; }

    IList<IFeedDefinition> Feeds { get; set; }

    IDictionary<string, List<FeedMessageDefinition>> FeedMessages { get; set; }

    IDictionary<string, List<string>> FeedsOfParticipant { get; set; }
}
