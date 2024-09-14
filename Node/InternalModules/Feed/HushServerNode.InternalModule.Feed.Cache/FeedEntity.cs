using System.Collections.Generic;

namespace HushServerNode.InternalModule.Feed.Cache;

public class FeedEntity
{
    public string FeedId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int FeedType { get; set; }

    public double BlockIndex { get; set; }

    public ICollection<FeedParticipants> FeedParticipants { get; set; }

    public FeedEntity()
    {
        this.FeedParticipants = new List<FeedParticipants>();
    }
}
