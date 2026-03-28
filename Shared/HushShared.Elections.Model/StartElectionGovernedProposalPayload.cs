using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushShared.Elections.Model;

public record StartElectionGovernedProposalPayload(
    ElectionId ElectionId,
    Guid ProposalId,
    ElectionGovernedActionType ActionType,
    string ActorPublicAddress) : ITransactionPayloadKind;

public static class StartElectionGovernedProposalPayloadHandler
{
    public static readonly Guid StartElectionGovernedProposalPayloadKind = new("5fb28a3a-bf04-44e1-aa18-aa75319f6e0f");

    public static UnsignedTransaction<StartElectionGovernedProposalPayload> CreateNew(
        ElectionId electionId,
        Guid proposalId,
        ElectionGovernedActionType actionType,
        string actorPublicAddress) =>
        UnsignedTransactionHandler.CreateNew(
            StartElectionGovernedProposalPayloadKind,
            Timestamp.Current,
            new StartElectionGovernedProposalPayload(
                electionId,
                proposalId,
                actionType,
                actorPublicAddress));
}
