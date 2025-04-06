namespace HushShared.Feeds.Model;

public static class FeedMessageIdHandler
{
    public static FeedMessageId CreateFromString(string value) => new(Guid.Parse(value));
}