namespace HushShared.Feeds.Model;

public static class FeedIdHandler
{
    public static FeedId CreateFromString(string value) => new(Guid.Parse(value));
}