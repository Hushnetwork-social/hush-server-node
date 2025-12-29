namespace HushShared.Feeds.Model;

/// <summary>
/// Represents a participant in a Group Feed with their encrypted key.
/// EncryptedFeedKey contains the feed's AES key encrypted with this participant's public encrypt key.
/// KeyGeneration tracks which key rotation this participant belongs to.
/// </summary>
public record GroupFeedParticipant(
    FeedId FeedId,
    string ParticipantPublicAddress,
    ParticipantType ParticipantType,
    string EncryptedFeedKey,
    int KeyGeneration);
