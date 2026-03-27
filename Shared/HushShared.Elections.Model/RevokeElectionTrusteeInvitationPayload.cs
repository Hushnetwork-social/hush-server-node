using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushShared.Elections.Model;

public record RevokeElectionTrusteeInvitationPayload(
    ElectionId ElectionId,
    Guid InvitationId,
    string ActorPublicAddress) : ITransactionPayloadKind;

public static class RevokeElectionTrusteeInvitationPayloadHandler
{
    public static readonly Guid RevokeElectionTrusteeInvitationPayloadKind = new("5161830b-5d77-42ef-9f30-fa140eb71559");

    public static UnsignedTransaction<RevokeElectionTrusteeInvitationPayload> CreateNew(
        ElectionId electionId,
        Guid invitationId,
        string actorPublicAddress) =>
        UnsignedTransactionHandler.CreateNew(
            RevokeElectionTrusteeInvitationPayloadKind,
            Timestamp.Current,
            new RevokeElectionTrusteeInvitationPayload(
                electionId,
                invitationId,
                actorPublicAddress));
}
