using HushShared.Blockchain.TransactionModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Member entry for AddMembersToCustomCircle payload.
/// </summary>
public record CustomCircleMember(string PublicAddress, string PublicEncryptAddress);

/// <summary>
/// Payload for adding one or more members to a custom circle.
/// FEAT-092: max 100 members per transaction, atomic failure on any invalid member.
/// </summary>
public record AddMembersToCustomCirclePayload(
    FeedId FeedId,
    string OwnerPublicAddress,
    CustomCircleMember[] Members) : ITransactionPayloadKind;

public static class AddMembersToCustomCirclePayloadHandler
{
    public static Guid AddMembersToCustomCirclePayloadKind { get; } = Guid.Parse("4d27a7d3-693f-4306-93cc-f4e3a562cdd4");
}
