using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushShared.Elections.Model;

public record CloseElectionPayload(
    ElectionId ElectionId,
    string ActorPublicAddress,
    byte[]? AcceptedBallotSetHash,
    byte[]? FinalEncryptedTallyHash) : ITransactionPayloadKind;

public static class CloseElectionPayloadHandler
{
    public static readonly Guid CloseElectionPayloadKind = new("16d0401b-41b5-4d7f-8603-119e28fb53b0");

    public static UnsignedTransaction<CloseElectionPayload> CreateNew(
        ElectionId electionId,
        string actorPublicAddress,
        byte[]? acceptedBallotSetHash,
        byte[]? finalEncryptedTallyHash) =>
        UnsignedTransactionHandler.CreateNew(
            CloseElectionPayloadKind,
            Timestamp.Current,
            new CloseElectionPayload(
                electionId,
                actorPublicAddress,
                acceptedBallotSetHash,
                finalEncryptedTallyHash));
}
