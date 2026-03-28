using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushShared.Elections.Model;

public record CreateElectionDraftPayload(
    ElectionId ElectionId,
    string OwnerPublicAddress,
    string SnapshotReason,
    ElectionDraftSpecification Draft) : ITransactionPayloadKind;

public static class CreateElectionDraftPayloadHandler
{
    public static Guid CreateElectionDraftPayloadKind { get; } = Guid.Parse("8d3b2f41-5e1f-4c7f-b08d-1a7c6f524001");

    public static UnsignedTransaction<CreateElectionDraftPayload> CreateNew(
        ElectionId electionId,
        string ownerPublicAddress,
        string snapshotReason,
        ElectionDraftSpecification draft) =>
        UnsignedTransactionHandler.CreateNew(
            CreateElectionDraftPayloadKind,
            Timestamp.Current,
            new CreateElectionDraftPayload(
                electionId,
                ownerPublicAddress,
                snapshotReason,
                draft));
}
