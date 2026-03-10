using HushShared.Feeds.Model;

namespace HushShared.Reactions.Model;

public static class PublicReactionScopes
{
    // Reserved membership scope for "all valid HushNetwork identities".
    public static readonly FeedId GlobalHushMembers = new(new Guid("00000000-0000-0000-0000-000000000001"));
}
