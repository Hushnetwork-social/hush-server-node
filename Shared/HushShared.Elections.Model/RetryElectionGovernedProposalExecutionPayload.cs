using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushShared.Elections.Model;

public record RetryElectionGovernedProposalExecutionPayload(
    ElectionId ElectionId,
    Guid ProposalId,
    string ActorPublicAddress) : ITransactionPayloadKind;

public static class RetryElectionGovernedProposalExecutionPayloadHandler
{
    public static readonly Guid RetryElectionGovernedProposalExecutionPayloadKind = new("47da657b-30b9-4d06-bae0-157048ff8cb4");

    public static UnsignedTransaction<RetryElectionGovernedProposalExecutionPayload> CreateNew(
        ElectionId electionId,
        Guid proposalId,
        string actorPublicAddress) =>
        UnsignedTransactionHandler.CreateNew(
            RetryElectionGovernedProposalExecutionPayloadKind,
            Timestamp.Current,
            new RetryElectionGovernedProposalExecutionPayload(
                electionId,
                proposalId,
                actorPublicAddress));
}
