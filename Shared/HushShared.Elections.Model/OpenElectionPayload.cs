using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushShared.Elections.Model;

public record OpenElectionPayload(
    ElectionId ElectionId,
    string ActorPublicAddress,
    ElectionWarningCode[] RequiredWarningCodes,
    byte[]? FrozenEligibleVoterSetHash,
    string? TrusteePolicyExecutionReference,
    string? ReportingPolicyExecutionReference,
    string? ReviewWindowExecutionReference) : ITransactionPayloadKind;

public static class OpenElectionPayloadHandler
{
    public static readonly Guid OpenElectionPayloadKind = new("7f4e60ef-2a88-4794-9705-63f4721a0f7b");

    public static UnsignedTransaction<OpenElectionPayload> CreateNew(
        ElectionId electionId,
        string actorPublicAddress,
        ElectionWarningCode[] requiredWarningCodes,
        byte[]? frozenEligibleVoterSetHash,
        string? trusteePolicyExecutionReference,
        string? reportingPolicyExecutionReference,
        string? reviewWindowExecutionReference) =>
        UnsignedTransactionHandler.CreateNew(
            OpenElectionPayloadKind,
            Timestamp.Current,
            new OpenElectionPayload(
                electionId,
                actorPublicAddress,
                requiredWarningCodes,
                frozenEligibleVoterSetHash,
                trusteePolicyExecutionReference,
                reportingPolicyExecutionReference,
                reviewWindowExecutionReference));
}
