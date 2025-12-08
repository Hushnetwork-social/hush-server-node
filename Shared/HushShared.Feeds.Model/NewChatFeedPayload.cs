using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

public record NewChatFeedPayload(
    FeedId FeedId,
    FeedType FeedType,
    ChatFeedParticipant[] FeedParticipants) : ITransactionPayloadKind;

/// <summary>
/// Participant data for a new chat feed transaction.
/// EncryptedFeedKey contains the feed's AES key encrypted with this participant's RSA public key.
/// </summary>
public record ChatFeedParticipant(
    FeedId FeedId,
    string ParticipantPublicAddress,
    string EncryptedFeedKey);
