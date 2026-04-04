using HushNetwork.proto;
using HushShared.Elections.Model;

namespace HushNode.Elections.gRPC;

public interface IElectionQueryApplicationService
{
    Task<GetElectionResponse> GetElectionAsync(ElectionId electionId);

    Task<SearchElectionDirectoryResponse> SearchElectionDirectoryAsync(
        string searchTerm,
        IReadOnlyCollection<string>? ownerPublicAddresses,
        int limit,
        string actorPublicAddress);

    Task<GetElectionHubViewResponse> GetElectionHubViewAsync(string actorPublicAddress);

    Task<GetElectionEligibilityViewResponse> GetElectionEligibilityViewAsync(ElectionId electionId, string actorPublicAddress);

    Task<GetElectionVotingViewResponse> GetElectionVotingViewAsync(
        ElectionId electionId,
        string actorPublicAddress,
        string? submissionIdempotencyKey);

    Task<VerifyElectionReceiptResponse> VerifyElectionReceiptAsync(
        ElectionId electionId,
        string actorPublicAddress,
        string receiptId,
        string acceptanceId,
        string serverProof);

    Task<GetElectionEnvelopeAccessResponse> GetElectionEnvelopeAccessAsync(ElectionId electionId, string actorPublicAddress);

    Task<GetElectionResultViewResponse> GetElectionResultViewAsync(ElectionId electionId, string actorPublicAddress);

    Task<GetElectionReportAccessGrantsResponse> GetElectionReportAccessGrantsAsync(ElectionId electionId, string actorPublicAddress);

    Task<GetElectionCeremonyActionViewResponse> GetElectionCeremonyActionViewAsync(ElectionId electionId, string actorPublicAddress);

    Task<GetElectionsByOwnerResponse> GetElectionsByOwnerAsync(string ownerPublicAddress);
}
