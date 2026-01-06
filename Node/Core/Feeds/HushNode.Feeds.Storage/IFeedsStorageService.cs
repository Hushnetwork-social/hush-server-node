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
    /// Update group feed settings (title, description, visibility) in a single operation.
    /// Only non-null values are updated.
    /// </summary>
    Task UpdateGroupFeedSettingsAsync(FeedId feedId, string? newTitle, string? newDescription, bool? isPublic);

    /// <summary>
    /// Soft-delete a group feed (mark as deleted, preserve data).
    /// </summary>
    Task MarkGroupFeedDeletedAsync(FeedId feedId);

    // ===== Group Feed Join/Leave Operations (FEAT-008) =====

    /// <summary>
    /// Add a new participant to a group feed.
    /// </summary>
    Task AddParticipantAsync(FeedId feedId, GroupFeedParticipantEntity participant);

    /// <summary>
    /// Update participant leave status (set LeftAtBlock and LastLeaveBlock).
    /// </summary>
    Task UpdateParticipantLeaveStatusAsync(FeedId feedId, string publicAddress, BlockIndex leftAtBlock);

    /// <summary>
    /// Update participant on rejoin (clear LeftAtBlock, update JoinedAtBlock and ParticipantType).
    /// </summary>
    Task UpdateParticipantRejoinAsync(FeedId feedId, string publicAddress, BlockIndex joinedAtBlock, ParticipantType participantType);

    /// <summary>
    /// Update participant status when they are banned (set LeftAtBlock, LastLeaveBlock, and ParticipantType to Banned).
    /// </summary>
    Task UpdateParticipantBanAsync(FeedId feedId, string publicAddress, BlockIndex bannedAtBlock);

    /// <summary>
    /// Update participant status when they are unbanned (clear LeftAtBlock, update JoinedAtBlock, set ParticipantType to Member).
    /// </summary>
    Task UpdateParticipantUnbanAsync(FeedId feedId, string publicAddress, BlockIndex rejoinedAtBlock);

    /// <summary>
    /// Check if a user is banned from a group feed.
    /// Returns true if the user exists, has ParticipantType.Banned, and LeftAtBlock is set.
    /// </summary>
    Task<bool> IsBannedAsync(FeedId feedId, string publicAddress);

    /// <summary>
    /// Get a participant including those who have left (for cooldown checking).
    /// Unlike GetGroupFeedParticipantAsync, this returns participants regardless of LeftAtBlock.
    /// </summary>
    Task<GroupFeedParticipantEntity?> GetParticipantWithHistoryAsync(FeedId feedId, string publicAddress);

    /// <summary>
    /// Get all active participants in a group (not left, not banned).
    /// Used for key rotation to know who should receive the new key.
    /// </summary>
    Task<IReadOnlyList<GroupFeedParticipantEntity>> GetActiveParticipantsAsync(FeedId feedId);

    /// <summary>
    /// Get all participants in a group (including those who left or were banned).
    /// Used for displaying historical membership events in the chat.
    /// </summary>
    Task<IReadOnlyList<GroupFeedParticipantEntity>> GetAllParticipantsAsync(FeedId feedId);

    /// <summary>
    /// Add a new key generation with encrypted keys to a group feed.
    /// Also increments the group's CurrentKeyGeneration.
    /// </summary>
    Task AddKeyGenerationAsync(FeedId feedId, GroupFeedKeyGenerationEntity keyGeneration);

    // ===== Key Rotation Operations (FEAT-010) =====

    /// <summary>
    /// Get the maximum KeyGeneration for a group feed.
    /// Returns null if the group has no key generations.
    /// </summary>
    Task<int?> GetMaxKeyGenerationAsync(FeedId feedId);

    /// <summary>
    /// Get addresses of all active group members who should receive new keys during rotation.
    /// Includes: Admin, Member, Blocked (still in group).
    /// Excludes: Banned (removed from group), users with LeftAtBlock != null.
    /// </summary>
    Task<IReadOnlyList<string>> GetActiveGroupMemberAddressesAsync(FeedId feedId);

    /// <summary>
    /// Atomically creates a new KeyGeneration with all encrypted keys and updates the group's CurrentKeyGeneration.
    /// The KeyGeneration entity should have EncryptedKeys collection populated.
    /// </summary>
    Task CreateKeyRotationAsync(GroupFeedKeyGenerationEntity keyGeneration);

    // ===== Group Messaging Operations (FEAT-011) =====

    /// <summary>
    /// Get a specific KeyGeneration entity by its number.
    /// Used for grace period validation during message submission.
    /// </summary>
    Task<GroupFeedKeyGenerationEntity?> GetKeyGenerationByNumberAsync(FeedId feedId, int keyGeneration);

    /// <summary>
    /// Check if a user is an active member who can send messages.
    /// Returns true for Admin or Member, false for Blocked, Banned, Left, or non-member.
    /// </summary>
    Task<bool> CanMemberSendMessagesAsync(FeedId feedId, string publicAddress);

    // ===== Group Feed Query Operations (FEAT-017) =====

    /// <summary>
    /// Get all KeyGenerations for a group feed that the user has access to.
    /// Returns KeyGenerations where the user was a member when the key was created.
    /// </summary>
    Task<IReadOnlyList<GroupFeedKeyGenerationEntity>> GetKeyGenerationsForUserAsync(FeedId feedId, string publicAddress);

    /// <summary>
    /// Update the BlockIndex of a feed.
    /// Used to signal to clients that the feed has changed (e.g., membership change).
    /// </summary>
    Task UpdateFeedBlockIndexAsync(FeedId feedId, BlockIndex blockIndex);

    // ===== Public Group Search Operations =====

    /// <summary>
    /// Search for public groups by title or description (case-insensitive partial match).
    /// Returns groups where IsPublic = true and title or description contains the search query.
    /// </summary>
    Task<IReadOnlyList<GroupFeed>> SearchPublicGroupsAsync(string searchQuery, int maxResults = 20);
}