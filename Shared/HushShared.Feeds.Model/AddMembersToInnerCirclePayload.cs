using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Member entry for AddMembersToInnerCircle payload.
/// PublicEncryptAddress is required to encrypt rotated keys for new members.
/// </summary>
public record InnerCircleMember(string PublicAddress, string PublicEncryptAddress);

/// <summary>
/// Payload for adding one or more members to an owner's Inner Circle.
/// </summary>
public record AddMembersToInnerCirclePayload(
    string OwnerPublicAddress,
    InnerCircleMember[] Members) : ITransactionPayloadKind;

public static class AddMembersToInnerCirclePayloadHandler
{
    public static Guid AddMembersToInnerCirclePayloadKind { get; } = Guid.Parse("f1baf6ab-cd4f-4d95-a0f2-7d1abcc4f8e4");
}
