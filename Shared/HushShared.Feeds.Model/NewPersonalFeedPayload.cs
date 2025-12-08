using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Payload for creating a personal feed.
/// EncryptedFeedKey contains the feed's AES key encrypted with the owner's RSA public key.
/// </summary>
public record NewPersonalFeedPayload(
    FeedId FeedId,
    string Title,
    FeedType FeedType,
    string EncryptedFeedKey) : ITransactionPayloadKind;
