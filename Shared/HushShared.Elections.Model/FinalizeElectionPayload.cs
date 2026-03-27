using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushShared.Elections.Model;

public record FinalizeElectionPayload(
    ElectionId ElectionId,
    string ActorPublicAddress,
    byte[]? AcceptedBallotSetHash,
    byte[]? FinalEncryptedTallyHash) : ITransactionPayloadKind;

public static class FinalizeElectionPayloadHandler
{
    public static readonly Guid FinalizeElectionPayloadKind = new("ca90e62d-8bcb-4764-9386-70d74fb75627");

    public static UnsignedTransaction<FinalizeElectionPayload> CreateNew(
        ElectionId electionId,
        string actorPublicAddress,
        byte[]? acceptedBallotSetHash,
        byte[]? finalEncryptedTallyHash) =>
        UnsignedTransactionHandler.CreateNew(
            FinalizeElectionPayloadKind,
            Timestamp.Current,
            new FinalizeElectionPayload(
                electionId,
                actorPublicAddress,
                acceptedBallotSetHash,
                finalEncryptedTallyHash));
}
