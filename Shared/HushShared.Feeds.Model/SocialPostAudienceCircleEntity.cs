using Olimpo;
namespace HushShared.Feeds.Model;

public class SocialPostAudienceCircleEntity
{
    public Guid PostId { get; set; }

    public FeedId CircleFeedId { get; set; }

    public SocialPostEntity Post { get; set; } = null!;
}
