namespace HushShared.Feeds.Model;

public record FeedParticipant(
    FeedId FeedId,
    string ParticipantPublicAddress,
    ParticipantType ParticipantType,
    string FeedPublicEncryptAddress,
    string FeedPrivateEncryptKey)
{
    public required virtual Feed Feed { get; init; }
}