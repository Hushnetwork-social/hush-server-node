using System.Text.Json;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushShared.Elections.Model;

public record EncryptedElectionEnvelopePayload(
    ElectionId ElectionId,
    string EnvelopeVersion,
    string NodeEncryptedElectionPrivateKey,
    string ActorEncryptedElectionPrivateKey,
    string EncryptedPayload) : ITransactionPayloadKind;

public record EncryptedElectionActionEnvelope(
    string ActionType,
    JsonElement ActionPayload);

public record CreateElectionDraftActionPayload(
    string OwnerPublicAddress,
    string SnapshotReason,
    ElectionDraftSpecification Draft);

public record UpdateElectionDraftActionPayload(
    string ActorPublicAddress,
    string SnapshotReason,
    ElectionDraftSpecification Draft);

public record InviteElectionTrusteeActionPayload(
    Guid InvitationId,
    string ActorPublicAddress,
    string TrusteeUserAddress,
    string TrusteeEncryptedElectionPrivateKey,
    string? TrusteeDisplayName);

public static class EncryptedElectionEnvelopeActionTypes
{
    public const string CreateDraft = "create_draft";
    public const string UpdateDraft = "update_draft";
    public const string InviteTrustee = "invite_trustee";
}

public static class EncryptedElectionEnvelopePayloadHandler
{
    public static Guid EncryptedElectionEnvelopePayloadKind { get; } =
        Guid.Parse("e839953b-dc29-4d81-a44e-9694f6614943");

    public const string CurrentEnvelopeVersion = "election-envelope-v1";

    public static UnsignedTransaction<EncryptedElectionEnvelopePayload> CreateNew(
        ElectionId electionId,
        string envelopeVersion,
        string nodeEncryptedElectionPrivateKey,
        string actorEncryptedElectionPrivateKey,
        string encryptedPayload) =>
        UnsignedTransactionHandler.CreateNew(
            EncryptedElectionEnvelopePayloadKind,
            Timestamp.Current,
            new EncryptedElectionEnvelopePayload(
                electionId,
                envelopeVersion,
                nodeEncryptedElectionPrivateKey,
                actorEncryptedElectionPrivateKey,
                encryptedPayload));
}
