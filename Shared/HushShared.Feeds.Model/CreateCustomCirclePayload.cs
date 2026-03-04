using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Payload for creating a custom owner-managed circle.
/// FEAT-092: Circle name is validated server-side (3-40 chars, allowed charset, owner-unique case-insensitive).
/// </summary>
public record CreateCustomCirclePayload(
    FeedId FeedId,
    string OwnerPublicAddress,
    string CircleName) : ITransactionPayloadKind;

public static class CreateCustomCirclePayloadHandler
{
    public static Guid CreateCustomCirclePayloadKind { get; } = Guid.Parse("8f7d8cc0-f8fb-4f8f-b2d6-7e51db96f351");
}
