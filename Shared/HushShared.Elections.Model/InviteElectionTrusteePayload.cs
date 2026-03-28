using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushShared.Elections.Model;

public record InviteElectionTrusteePayload(
    ElectionId ElectionId,
    Guid InvitationId,
    string ActorPublicAddress,
    string TrusteeUserAddress,
    string? TrusteeDisplayName) : ITransactionPayloadKind;

public static class InviteElectionTrusteePayloadHandler
{
    public static Guid InviteElectionTrusteePayloadKind { get; } = Guid.Parse("e78e2be3-b507-41ef-bef2-5f9427ed763f");

    public static UnsignedTransaction<InviteElectionTrusteePayload> CreateNew(
        ElectionId electionId,
        Guid invitationId,
        string actorPublicAddress,
        string trusteeUserAddress,
        string? trusteeDisplayName) =>
        UnsignedTransactionHandler.CreateNew(
            InviteElectionTrusteePayloadKind,
            Timestamp.Current,
            new InviteElectionTrusteePayload(
                electionId,
                invitationId,
                actorPublicAddress,
                trusteeUserAddress,
                trusteeDisplayName));
}
