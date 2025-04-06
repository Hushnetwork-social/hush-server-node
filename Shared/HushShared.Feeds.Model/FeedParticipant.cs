namespace HushShared.Feeds.Model;

public record FeedParticipant(
    FeedId FeedId,
    string ParticipantPublicAddress,
    ParticipantType ParticipantType,
    string FeedPublicEncryptAddress,
    string FeedPrivateEncryptKey);

public enum ParticipantType
{
    Owner = 0,
    Member = 1,
    Guest = 2
}
