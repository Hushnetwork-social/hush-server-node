using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

public record NewChatFeedPayload(
    FeedId FeedId,
    FeedType FeedType,
    ChatFeedParticipant[] FeedParticipants) : ITransactionPayloadKind;

public record ChatFeedParticipant(
    FeedId FeedId,
    string ParticipantPublicAddress,
    string FeedPublicEncryptAddress,
    string FeedPrivateEncryptKey);
