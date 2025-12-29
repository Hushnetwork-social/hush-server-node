using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

public interface IFeedsStorageService
{
    /// <summary>
    /// Creates a new Group Feed with all related entities (participants, key generation, encrypted keys).
    /// </summary>
    Task CreateGroupFeed(GroupFeed groupFeed);
    Task<bool> HasPersonalFeed(string publicSigningAddress);

    Task<bool> IsFeedIsBlockchain(FeedId feedId);

    Task CreateFeed(Feed feed);

    /// <summary>
    /// Atomically creates a personal feed if one doesn't exist for the user.
    /// Returns true if created, false if personal feed already exists.
    /// </summary>
    Task<bool> CreatePersonalFeedIfNotExists(Feed feed, string publicSigningAddress);

    Task<IEnumerable<Feed>> RetrieveFeedsForAddress(string publicSigningAddress, BlockIndex blockIndex);

    Task<Feed?> GetFeedByIdAsync(FeedId feedId);

    /// <summary>
    /// Get all feed IDs that a user is a participant of.
    /// Used for reaction sync to know which feeds to query for updated tallies.
    /// </summary>
    Task<IReadOnlyList<FeedId>> GetFeedIdsForUserAsync(string publicAddress);

    /// <summary>
    /// Update the BlockIndex of all feeds where the user is a participant.
    /// Called when a user updates their identity so other clients can detect the change.
    /// </summary>
    Task UpdateFeedsBlockIndexForParticipantAsync(string publicSigningAddress, BlockIndex blockIndex);
}