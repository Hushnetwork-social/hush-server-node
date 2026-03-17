using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public interface ISocialThreadService
{
    Task<SocialThreadAccessResult> AuthorizeAsync(
        Guid postId,
        string? requesterPublicAddress,
        bool isAuthenticated,
        SocialThreadAccessMode accessMode);

    Task<(SocialThreadAccessResult Access, SocialThreadEntryContract? ThreadEntry)> ResolveThreadEntryAsync(
        Guid postId,
        FeedMessageId entryId,
        FeedMessageId? requestedReplyTargetId);

    Task<SocialThreadPageResult> GetCommentsPageAsync(
        Guid postId,
        string? requesterPublicAddress,
        bool isAuthenticated,
        int? limit,
        FeedMessageId? beforeEntryId);

    Task<SocialThreadPageResult> GetRepliesPageAsync(
        Guid postId,
        FeedMessageId threadRootId,
        string? requesterPublicAddress,
        bool isAuthenticated,
        int? limit,
        FeedMessageId? beforeEntryId);
}
