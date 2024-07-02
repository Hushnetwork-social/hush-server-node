namespace HushServerNode.CacheService;
public class FeedParticipants
{
    public string FeedId { get; set; } = string.Empty;

    public string ParticipantPublicAddress { get; set; } = string.Empty;

    public int ParticipantType { get; set; }

    public FeedEntity Feed { get; set; }
}
