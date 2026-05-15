using HushNetwork.proto;
using HushShared.Elections.Model;

namespace HushNode.Elections.gRPC;

public interface IElectionQueryApplicationService
{
    Task<GetElectionResponse> GetElectionAsync(ElectionId electionId, string? actorPublicAddress = null);

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
        string serverProof,
        string? receiptCommitment = null,
        string? preparedBallotId = null);

    Task<GetElectionEnvelopeAccessResponse> GetElectionEnvelopeAccessAsync(ElectionId electionId, string actorPublicAddress);

    Task<GetElectionResultViewResponse> GetElectionResultViewAsync(ElectionId electionId, string actorPublicAddress);

    Task<GetElectionVerificationPackageStatusResponse> GetElectionVerificationPackageStatusAsync(
        ElectionId electionId,
        string actorPublicAddress);

    Task<ExportElectionVerificationPackageResponse> ExportElectionVerificationPackageAsync(
        ElectionId electionId,
        string actorPublicAddress,
        ElectionVerificationPackageViewProto packageView);

    Task<GetElectionReportAccessGrantsResponse> GetElectionReportAccessGrantsAsync(ElectionId electionId, string actorPublicAddress);

    Task<GetElectionCeremonyActionViewResponse> GetElectionCeremonyActionViewAsync(ElectionId electionId, string actorPublicAddress);

    Task<GetElectionsByOwnerResponse> GetElectionsByOwnerAsync(string ownerPublicAddress);

    Task<ElectionAnomalyOwnThreadProjection?> GetElectionAnomalyOwnThreadAsync(
        ElectionId electionId,
        string actorPublicAddress);

    Task<ElectionAnomalyOwnerTriageProjection?> GetElectionAnomalyOwnerTriageAsync(
        ElectionId electionId,
        string actorPublicAddress);

    Task<ElectionAnomalyTrusteeCountsProjection?> GetElectionAnomalyTrusteeCountsAsync(
        ElectionId electionId,
        string actorPublicAddress);

    Task<ElectionAnomalyAuditorRestrictedReviewProjection?> GetElectionAnomalyAuditorRestrictedReviewAsync(
        ElectionId electionId,
        string actorPublicAddress);

    Task<ElectionAnomalyEvidenceManifestProjection?> GetElectionAnomalyEvidenceManifestAsync(
        ElectionId electionId,
        string actorPublicAddress,
        string scopeId);

    Task<ElectionAnomalyReportManifestSeedProjection?> GetElectionAnomalyReportManifestSeedAsync(
        ElectionId electionId,
        string actorPublicAddress);
}
