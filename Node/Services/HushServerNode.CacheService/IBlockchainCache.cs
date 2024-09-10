using System.Collections.Generic;
using System.Threading.Tasks;

namespace HushServerNode.CacheService;

public interface IBlockchainCache
{
    void ConnectPersistentDatabase();

    // Task<BlockchainState> GetBlockchainStateAsync();

    // Task UpdateBlockchainState(BlockchainState blockchainState);

    // Task SaveBlockAsync(string blockId,
    //     long blockHeight,
    //     string previousBlockId,
    //     string nextBlockId,
    //     string blockHash,
    //     string blockJson);

    Task UpdateBalanceAsync(string address, double value);

    double GetBalance(string address);

    Task UpdateProfile(Profile profile);

    Profile? GetProfile(string address);

    FeedEntity? GetFeed(string feedId);

    bool FeedExists(string feedId);

    bool UserHasFeeds(string address);

    bool UserHasPersonalFeed(string address);

    Task CreatePersonalFeed(string feedTitle,
        string feedId,
        int feedType,
        string personalFeedOwnerAddress,
        string publicEncryptAddress,
        string privateEncryptKey,
        double blockIndex);

    Task CreateChatFeed(
        string feedId,
        int feedType,
        string chatParticipantAddress,
        string chatParticipantPublicEncryptAddress,
        string chatParticipantPrivateEncryptKey,
        double blockIndex);

    Task AddParticipantToChatFeed(
        string feedId,
        string chatParticipantAddress,
        string chatParticipantPublicEncryptAddress,
        string chatParticipantPrivateEncryptKey,
        double blockIndex);

    IEnumerable<FeedEntity> GetUserFeeds(string address);

    IEnumerable<FeedMessageEntity> GetFeedMessages(string feedId, double blockIndex);

    Task SaveMessageAsync(FeedMessageEntity feedMessage);
}
