using HushNode.Feeds.Storage;
using HushNode.Reactions.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public sealed class SocialThreadService(
    IFeedsStorageService feedsStorageService,
    IFeedMessageStorageService feedMessageStorageService,
    IReactionService reactionService) : ISocialThreadService
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IFeedMessageStorageService _feedMessageStorageService = feedMessageStorageService;
    private readonly IReactionService _reactionService = reactionService;

    public async Task<SocialThreadAccessResult> AuthorizeAsync(
        Guid postId,
        string? requesterPublicAddress,
        bool isAuthenticated,
        SocialThreadAccessMode accessMode)
    {
        var post = await _feedsStorageService.GetSocialPostAsync(postId);
        if (post == null)
        {
            return SocialThreadAccessResult.Denied(
                SocialThreadAccessErrorCode.PostNotFound,
                "Post not found.");
        }

        return await AuthorizeAsync(post, requesterPublicAddress, isAuthenticated, accessMode);
    }

    public async Task<(SocialThreadAccessResult Access, SocialThreadEntryContract? ThreadEntry)> ResolveThreadEntryAsync(
        Guid postId,
        FeedMessageId entryId,
        FeedMessageId? requestedReplyTargetId)
    {
        if (requestedReplyTargetId == null)
        {
            return (SocialThreadAccessResult.Allowed(), SocialThreadEntryContract.Comment(postId, entryId));
        }

        var targetMessage = await _feedMessageStorageService.GetFeedMessageByIdAsync(requestedReplyTargetId.Value);
        if (targetMessage == null || targetMessage.FeedId != new FeedId(postId))
        {
            return (SocialThreadAccessResult.Denied(
                SocialThreadAccessErrorCode.InvalidReplyTarget,
                "Reply target is invalid for this post."), null);
        }

        var threadRootId = targetMessage.ReplyToMessageId ?? targetMessage.FeedMessageId;
        return (
            SocialThreadAccessResult.Allowed(),
            SocialThreadEntryContract.Reply(postId, entryId, threadRootId, threadRootId));
    }

    public async Task<SocialThreadPageResult> GetCommentsPageAsync(
        Guid postId,
        string? requesterPublicAddress,
        bool isAuthenticated,
        int? limit,
        FeedMessageId? beforeEntryId)
    {
        return await GetPageAsync(
            postId,
            requesterPublicAddress,
            isAuthenticated,
            SocialThreadPageKind.TopLevelComments,
            beforeEntryId,
            limit,
            message => message.ReplyToMessageId == null,
            message => SocialThreadEntryContract.Comment(postId, message.FeedMessageId));
    }

    public async Task<SocialThreadPageResult> GetRepliesPageAsync(
        Guid postId,
        FeedMessageId threadRootId,
        string? requesterPublicAddress,
        bool isAuthenticated,
        int? limit,
        FeedMessageId? beforeEntryId)
    {
        return await GetPageAsync(
            postId,
            requesterPublicAddress,
            isAuthenticated,
            SocialThreadPageKind.ThreadReplies,
            beforeEntryId,
            limit,
            message => message.ReplyToMessageId == threadRootId,
            message => SocialThreadEntryContract.Reply(
                postId,
                message.FeedMessageId,
                threadRootId,
                threadRootId));
    }

    private async Task<SocialThreadPageResult> GetPageAsync(
        Guid postId,
        string? requesterPublicAddress,
        bool isAuthenticated,
        SocialThreadPageKind pageKind,
        FeedMessageId? beforeEntryId,
        int? limit,
        Func<FeedMessage, bool> filter,
        Func<FeedMessage, SocialThreadEntryContract> contractFactory)
    {
        var paging = SocialThreadPagingContractRules.For(pageKind);
        var access = await AuthorizeAsync(postId, requesterPublicAddress, isAuthenticated, SocialThreadAccessMode.Read);
        if (!access.IsAllowed)
        {
            return new SocialThreadPageResult(false, access.ErrorCode, access.Message, paging, Array.Empty<RankedSocialThreadEntry>(), false);
        }

        var feedId = new FeedId(postId);
        var allMessages = (await _feedMessageStorageService.RetrieveLastFeedMessagesForFeedAsync(feedId, new BlockIndex(0)))
            .ToList();

        var messages = allMessages
            .Where(filter)
            .ToList();

        if (messages.Count == 0)
        {
            return new SocialThreadPageResult(true, SocialThreadAccessErrorCode.None, string.Empty, paging, Array.Empty<RankedSocialThreadEntry>(), false);
        }

        var tallies = (await _reactionService.GetTalliesAsync(feedId, messages.Select(x => x.FeedMessageId)))
            .ToDictionary(x => x.MessageId, x => (long)x.TotalCount);

        var childReplyCounts = pageKind == SocialThreadPageKind.TopLevelComments
            ? allMessages
                .Where(message => message.ReplyToMessageId != null)
                .GroupBy(message => message.ReplyToMessageId!.Value)
                .ToDictionary(group => group.Key, group => group.Count())
            : new Dictionary<FeedMessageId, int>();

        var ranked = messages
            .Select(message => new RankedSocialThreadEntry(
                contractFactory(message),
                message,
                tallies.GetValueOrDefault(message.FeedMessageId, 0),
                childReplyCounts.GetValueOrDefault(message.FeedMessageId, 0)))
            .OrderByDescending(x => x.ReactionCount)
            .ThenByDescending(x => x.Message.Timestamp.Value)
            .ToList();

        var startIndex = 0;
        if (beforeEntryId != null)
        {
            var beforeIndex = ranked.FindIndex(x => x.Message.FeedMessageId == beforeEntryId.Value);
            if (beforeIndex >= 0)
            {
                startIndex = beforeIndex + 1;
            }
        }

        var pageSize = NormalizeLimit(pageKind, limit);
        var page = ranked.Skip(startIndex).Take(pageSize).ToArray();
        var hasMore = startIndex + page.Length < ranked.Count;

        return new SocialThreadPageResult(true, SocialThreadAccessErrorCode.None, string.Empty, paging, page, hasMore);
    }

    private async Task<SocialThreadAccessResult> AuthorizeAsync(
        SocialPostEntity post,
        string? requesterPublicAddress,
        bool isAuthenticated,
        SocialThreadAccessMode accessMode)
    {
        var requester = requesterPublicAddress?.Trim();
        if (post.AudienceVisibility == SocialPostVisibility.Open)
        {
            if (accessMode == SocialThreadAccessMode.Read)
            {
                return SocialThreadAccessResult.Allowed();
            }

            return isAuthenticated && !string.IsNullOrWhiteSpace(requester)
                ? SocialThreadAccessResult.Allowed()
                : SocialThreadAccessResult.Denied(
                    SocialThreadAccessErrorCode.AuthenticationRequired,
                    "Authentication is required to interact with this post.");
        }

        if (!isAuthenticated || string.IsNullOrWhiteSpace(requester))
        {
            return SocialThreadAccessResult.Denied(
                SocialThreadAccessErrorCode.AuthenticationRequired,
                "Authentication is required to access this private thread.");
        }

        if (string.Equals(post.AuthorPublicAddress, requester, StringComparison.Ordinal))
        {
            return SocialThreadAccessResult.Allowed();
        }

        var circleFeedIds = post.AudienceCircles.Select(x => x.CircleFeedId).ToArray();
        var hasCircleAccess = await _feedsStorageService.IsUserInAnyActiveCircleAsync(requester, circleFeedIds);
        return hasCircleAccess
            ? SocialThreadAccessResult.Allowed()
            : SocialThreadAccessResult.Denied(
                SocialThreadAccessErrorCode.AccessDenied,
                "You do not have permission to access this private thread.");
    }

    private static int NormalizeLimit(SocialThreadPageKind pageKind, int? limit)
    {
        var paging = SocialThreadPagingContractRules.For(pageKind);
        if (limit == null || limit <= 0)
        {
            return paging.InitialPageSize;
        }

        return Math.Min(limit.Value, 50);
    }
}
