namespace HushServerNode.InternalModule.Feed.Cache;
public class FeedParticipants
{
    public string FeedId { get; set; } = string.Empty;

    public string ParticipantPublicAddress { get; set; } = string.Empty;

    public int ParticipantType { get; set; }

    public string PublicEncryptAddress { get; set; } = string.Empty;

    public string PrivateEncryptKey { get; set; } = string.Empty;

    public FeedEntity Feed { get; set; }
}
