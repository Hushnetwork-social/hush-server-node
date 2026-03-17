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
