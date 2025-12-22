using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.Model;

namespace HushShared.Feeds.Model;

public record FeedMessage(
    FeedMessageId FeedMessageId,
    FeedId FeedId,
    string MessageContent,
    string IssuerPublicAddress,
    Timestamp Timestamp,
    BlockIndex BlockIndex,
    byte[]? AuthorCommitment = null);  // Protocol Omega: Poseidon hash of author's user_secret for ZK proof
