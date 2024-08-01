// using System.Collections.Generic;
// using HushEcosystem.Model.Blockchain;
// using HushEcosystem.Model.Rpc.Feeds;

// namespace HushServerNode.Blockchain;

// public class BlockchainIndexDb : IBlockchainIndexDb
// {
//     public IDictionary<string, List<VerifiedTransaction>> GroupedTransactions { get; set; }

//     public IList<IFeedDefinition> Feeds { get; set; }

//     public IDictionary<string, List<FeedMessageDefinition>> FeedMessages { get; set; }

//     public IDictionary<string, List<string>> FeedsOfParticipant { get; set; }

//     public IDictionary<string, List<string>> ParticipantsOfFeed { get; set; }

//     public BlockchainIndexDb()
//     {
//         this.GroupedTransactions = new Dictionary<string, List<VerifiedTransaction>>();
//         // this.Profiles = new List<HushUserProfile>();
//         this.Feeds = new List<IFeedDefinition>();
//         this.FeedMessages = new Dictionary<string, List<FeedMessageDefinition>>();
//         this.FeedsOfParticipant = new Dictionary<string, List<string>>();
//         this.ParticipantsOfFeed = new Dictionary<string, List<string>>();
//     }
// }
