namespace HushServerNode.InternalModule.Feed.Cache;

public class FeedEntity
{
    public string FeedId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int FeedType { get; set; }

    public long BlockIndex { get; set; }

    public ICollection<FeedParticipants> FeedParticipants { get; set; }

    public FeedEntity()
    {
        this.FeedParticipants = new List<FeedParticipants>();
    }
}
