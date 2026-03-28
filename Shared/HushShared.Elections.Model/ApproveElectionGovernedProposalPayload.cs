using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushShared.Elections.Model;

public record ApproveElectionGovernedProposalPayload(
    ElectionId ElectionId,
    Guid ProposalId,
    string ActorPublicAddress,
    string? ApprovalNote) : ITransactionPayloadKind;

public static class ApproveElectionGovernedProposalPayloadHandler
{
    public static readonly Guid ApproveElectionGovernedProposalPayloadKind = new("b3467772-6e53-4c85-a03c-b945f452f6de");

    public static UnsignedTransaction<ApproveElectionGovernedProposalPayload> CreateNew(
        ElectionId electionId,
        Guid proposalId,
        string actorPublicAddress,
        string? approvalNote) =>
        UnsignedTransactionHandler.CreateNew(
            ApproveElectionGovernedProposalPayloadKind,
            Timestamp.Current,
            new ApproveElectionGovernedProposalPayload(
                electionId,
                proposalId,
                actorPublicAddress,
                approvalNote));
}
