using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Payload for promoting a member to admin in a Group Feed.
/// </summary>
public record PromoteToAdminPayload(
    FeedId FeedId,
    string AdminPublicAddress,
    string MemberPublicAddress) : ITransactionPayloadKind;

public static class PromoteToAdminPayloadHandler
{
    public static Guid PromoteToAdminPayloadKind { get; } = Guid.Parse("c9d0e1f2-a3b4-2c3d-6e7f-8a9b0c1d2e3f");
}
