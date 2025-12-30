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

    // ===== Group Feed Admin Operations (FEAT-009) =====

    /// <summary>
    /// Get a GroupFeed by its FeedId, including participants.
    /// </summary>
    Task<GroupFeed?> GetGroupFeedAsync(FeedId feedId);

    /// <summary>
    /// Get a specific participant from a group feed.
    /// </summary>
    Task<GroupFeedParticipantEntity?> GetGroupFeedParticipantAsync(FeedId feedId, string publicAddress);

    /// <summary>
    /// Update a participant's type (e.g., Member -> Blocked, Blocked -> Member).
    /// </summary>
    Task UpdateParticipantTypeAsync(FeedId feedId, string publicAddress, ParticipantType newType);

    /// <summary>
    /// Check if a user is an admin in the specified group.
    /// </summary>
    Task<bool> IsAdminAsync(FeedId feedId, string publicAddress);

    /// <summary>
    /// Get the count of admins in a group.
    /// </summary>
    Task<int> GetAdminCountAsync(FeedId feedId);

    // ===== Group Feed Metadata Operations (FEAT-009 Phase 4) =====

    /// <summary>
    /// Update the title of a group feed.
    /// </summary>
    Task UpdateGroupFeedTitleAsync(FeedId feedId, string newTitle);

    /// <summary>
    /// Update the description of a group feed.
    /// </summary>
    Task UpdateGroupFeedDescriptionAsync(FeedId feedId, string newDescription);

    /// <summary>
    /// Soft-delete a group feed (mark as deleted, preserve data).
    /// </summary>
    Task MarkGroupFeedDeletedAsync(FeedId feedId);

    // ===== Key Rotation Operations (FEAT-010) =====

    /// <summary>
    /// Get the maximum KeyGeneration for a group feed.
    /// Returns null if the group has no key generations.
    /// </summary>
    Task<int?> GetMaxKeyGenerationAsync(FeedId feedId);
}