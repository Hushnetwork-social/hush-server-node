using HushNetwork.proto;
using HushShared.Elections.Model;

namespace HushNode.Elections.gRPC;

public interface IElectionQueryApplicationService
{
    Task<GetElectionResponse> GetElectionAsync(ElectionId electionId);

    Task<GetElectionEligibilityViewResponse> GetElectionEligibilityViewAsync(ElectionId electionId, string actorPublicAddress);

    Task<GetElectionVotingViewResponse> GetElectionVotingViewAsync(
        ElectionId electionId,
        string actorPublicAddress,
        string? submissionIdempotencyKey);

    Task<GetElectionEnvelopeAccessResponse> GetElectionEnvelopeAccessAsync(ElectionId electionId, string actorPublicAddress);

    Task<GetElectionResultViewResponse> GetElectionResultViewAsync(ElectionId electionId, string actorPublicAddress);

    Task<GetElectionCeremonyActionViewResponse> GetElectionCeremonyActionViewAsync(ElectionId electionId, string actorPublicAddress);

    Task<GetElectionsByOwnerResponse> GetElectionsByOwnerAsync(string ownerPublicAddress);
}
