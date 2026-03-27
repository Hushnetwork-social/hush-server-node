using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushShared.Elections.Model;

public record UpdateElectionDraftPayload(
    ElectionId ElectionId,
    string ActorPublicAddress,
    string SnapshotReason,
    ElectionDraftSpecification Draft) : ITransactionPayloadKind;

public static class UpdateElectionDraftPayloadHandler
{
    public static readonly Guid UpdateElectionDraftPayloadKind = new("3ff677e5-9d53-45e6-bd03-16d494dff27b");

    public static UnsignedTransaction<UpdateElectionDraftPayload> CreateNew(
        ElectionId electionId,
        string actorPublicAddress,
        string snapshotReason,
        ElectionDraftSpecification draft) =>
        UnsignedTransactionHandler.CreateNew(
            UpdateElectionDraftPayloadKind,
            Timestamp.Current,
            new UpdateElectionDraftPayload(
                electionId,
                actorPublicAddress,
                snapshotReason,
                draft));
}
