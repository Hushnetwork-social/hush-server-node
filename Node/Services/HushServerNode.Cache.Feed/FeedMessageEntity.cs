namespace HushServerNode.Cache.Feed;

public class FeedMessageEntity
{
    public string FeedMessageId { get; set; } = string.Empty;

    public string FeedId { get; set; } = string.Empty;

    public string MessageContent { get; set; } = string.Empty;

    public string IssuerPublicAddress { get; set; } = string.Empty;

    public string IssuerName { get; set; } = string.Empty;

    public DateTime TimeStamp { get; set; }

    public long BlockIndex { get; set; }
}
