using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

/// <summary>
/// Result type for paginated message queries.
/// </summary>
/// <param name="Messages">The messages in this page, ordered by BlockIndex ascending.</param>
/// <param name="HasMoreMessages">True if older messages exist before this batch.</param>
/// <param name="OldestBlockIndex">Block index of the first (oldest) message in the returned array. 0 if no messages.</param>
public record PaginatedMessagesResult(
    IReadOnlyList<FeedMessage> Messages,
    bool HasMoreMessages,
    BlockIndex OldestBlockIndex);
