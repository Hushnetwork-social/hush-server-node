using HushShared.Feeds.Model;

namespace HushShared.Reactions.Model;

public enum ReactionTargetType
{
    Post = 0,
    Comment = 1,
    Reply = 2
}

public readonly record struct ReactionTarget(
    ReactionTargetType Type,
    Guid TargetId)
{
    public static ReactionTarget Post(Guid postId) => new(ReactionTargetType.Post, postId);

    public static ReactionTarget Comment(Guid commentId) => new(ReactionTargetType.Comment, commentId);

    public static ReactionTarget Reply(Guid replyId) => new(ReactionTargetType.Reply, replyId);
}

public static class Feat087ReactionTargetContract
{
    public static bool TryMapToMessageId(ReactionTarget target, out FeedMessageId messageId)
    {
        if (target.Type is ReactionTargetType.Post or ReactionTargetType.Comment or ReactionTargetType.Reply)
        {
            messageId = new FeedMessageId(target.TargetId);
            return true;
        }

        messageId = default;
        return false;
    }
}
