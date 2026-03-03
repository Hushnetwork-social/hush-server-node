using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Payload for creating the owner's Inner Circle group.
/// Inner Circle is unique per owner and cannot be deleted.
/// </summary>
public record CreateInnerCirclePayload(string OwnerPublicAddress) : ITransactionPayloadKind;

public static class CreateInnerCirclePayloadHandler
{
    public static Guid CreateInnerCirclePayloadKind { get; } = Guid.Parse("98e2518b-9e5b-4be5-b0d3-e2a57a86d601");
}
