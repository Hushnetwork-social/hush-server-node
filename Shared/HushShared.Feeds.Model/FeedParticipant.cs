namespace HushShared.Feeds.Model;

/// <summary>
/// Represents a participant in a feed with their encrypted copy of the feed's AES key.
/// The EncryptedFeedKey is the feed's AES-256 key encrypted with this participant's RSA public key,
/// allowing only this participant to decrypt it with their private RSA key.
/// </summary>
public record FeedParticipant(
    FeedId FeedId,
    string ParticipantPublicAddress,
    ParticipantType ParticipantType,
    string EncryptedFeedKey)
{
    public required virtual Feed Feed { get; init; }
}