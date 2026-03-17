namespace HushShared.Feeds.Model;

public enum SocialThreadEntryKind
{
    Comment = 0,
    Reply = 1
}

public enum SocialThreadPageKind
{
    TopLevelComments = 0,
    ThreadReplies = 1
}

public enum SocialThreadAccessMode
{
    Read = 0,
    Write = 1
}

public enum SocialThreadAccessErrorCode
{
    None = 0,
    PostNotFound = 1,
    AuthenticationRequired = 2,
    AccessDenied = 3,
    InvalidReplyTarget = 4
}

public record SocialThreadEntryContract(
    Guid PostId,
    FeedMessageId EntryId,
    SocialThreadEntryKind Kind,
    FeedMessageId? ParentCommentId,
    FeedMessageId ThreadRootId)
{
    public static SocialThreadEntryContract Comment(Guid postId, FeedMessageId commentId) =>
        new(
            PostId: postId,
            EntryId: commentId,
            Kind: SocialThreadEntryKind.Comment,
            ParentCommentId: null,
            ThreadRootId: commentId);

    public static SocialThreadEntryContract Reply(
        Guid postId,
        FeedMessageId replyId,
        FeedMessageId parentCommentId,
        FeedMessageId threadRootId) =>
        new(
            PostId: postId,
            EntryId: replyId,
            Kind: SocialThreadEntryKind.Reply,
            ParentCommentId: parentCommentId,
            ThreadRootId: threadRootId);

    public bool IsTopLevelComment => Kind == SocialThreadEntryKind.Comment;

    public bool IsReply => Kind == SocialThreadEntryKind.Reply;
}

public record SocialThreadPageContract(
    SocialThreadPageKind PageKind,
    int InitialPageSize,
    int LoadMorePageSize);

public record SocialThreadAccessResult(
    bool IsAllowed,
    SocialThreadAccessErrorCode ErrorCode,
    string Message)
{
    public static SocialThreadAccessResult Allowed() => new(true, SocialThreadAccessErrorCode.None, string.Empty);

    public static SocialThreadAccessResult Denied(SocialThreadAccessErrorCode errorCode, string message) =>
        new(false, errorCode, message);
}

public record RankedSocialThreadEntry(
    SocialThreadEntryContract ThreadEntry,
    FeedMessage Message,
    long ReactionCount);

public record SocialThreadPageResult(
    bool Success,
    SocialThreadAccessErrorCode ErrorCode,
    string Message,
    SocialThreadPageContract Paging,
    IReadOnlyList<RankedSocialThreadEntry> Entries,
    bool HasMore);

public static class SocialThreadPagingContractRules
{
    public const int InitialCommentPageSize = 10;
    public const int LoadMoreCommentPageSize = 10;
    public const int InitialReplyPageSize = 5;
    public const int LoadMoreReplyPageSize = 5;

    public static SocialThreadPageContract For(SocialThreadPageKind pageKind) =>
        pageKind switch
        {
            SocialThreadPageKind.TopLevelComments => new SocialThreadPageContract(
                pageKind,
                InitialCommentPageSize,
                LoadMoreCommentPageSize),
            SocialThreadPageKind.ThreadReplies => new SocialThreadPageContract(
                pageKind,
                InitialReplyPageSize,
                LoadMoreReplyPageSize),
            _ => throw new ArgumentOutOfRangeException(nameof(pageKind), pageKind, "Unsupported thread page kind.")
        };
}
