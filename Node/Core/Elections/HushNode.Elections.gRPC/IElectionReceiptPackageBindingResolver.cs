using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;

namespace HushNode.Elections.gRPC;

public interface IElectionReceiptPackageBindingResolver
{
    Task<ElectionReceiptPublicPackageBindingResult> ResolvePublicPackageBindingAsync(
        IElectionsRepository repository,
        ElectionRecord election);
}

public sealed record ElectionReceiptPublicPackageBindingResult(
    bool IsAvailable,
    string PackageId,
    string PackageHash,
    string VerifierProfileId,
    string UnavailableReason)
{
    public static ElectionReceiptPublicPackageBindingResult Available(
        string packageId,
        string packageHash,
        string verifierProfileId) =>
        new(true, packageId, packageHash, verifierProfileId, string.Empty);

    public static ElectionReceiptPublicPackageBindingResult Unavailable(string reason) =>
        new(false, string.Empty, string.Empty, string.Empty, reason);
}

public sealed class ElectionReceiptPackageBindingResolver(
    IElectionVerificationPackageExportService verificationPackageExportService)
    : IElectionReceiptPackageBindingResolver
{
    public async Task<ElectionReceiptPublicPackageBindingResult> ResolvePublicPackageBindingAsync(
        IElectionsRepository repository,
        ElectionRecord election)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(election);

        if (election.LifecycleState != ElectionLifecycleState.Finalized)
        {
            return ElectionReceiptPublicPackageBindingResult.Unavailable("election_not_finalized");
        }

        if (election.BallotDefinitionVersion is null ||
            election.BallotDefinitionHash is not { Length: > 0 } ||
            election.BallotDefinitionSealedAt is null)
        {
            return ElectionReceiptPublicPackageBindingResult.Unavailable("sealed_ballot_definition_missing");
        }

        var reportPackage = await repository.GetLatestReportPackageAsync(election.ElectionId);
        if (reportPackage?.Status != ElectionReportPackageStatus.Sealed)
        {
            return ElectionReceiptPublicPackageBindingResult.Unavailable("public_package_missing");
        }

        var protocolPackageBinding = await repository.GetSealedProtocolPackageBindingAsync(election.ElectionId) ??
            await repository.GetLatestProtocolPackageBindingAsync(election.ElectionId);
        if (protocolPackageBinding?.Status != ProtocolPackageBindingStatus.Sealed)
        {
            return ElectionReceiptPublicPackageBindingResult.Unavailable("protocol_package_binding_missing");
        }

        var finalizationSessions = await repository.GetFinalizationSessionsAsync(election.ElectionId);
        var finalizationShares = new List<ElectionFinalizationShareRecord>();
        foreach (var session in finalizationSessions)
        {
            finalizationShares.AddRange(await repository.GetFinalizationSharesAsync(session.Id));
        }

        var request = new ElectionVerificationPackageExportRequest(
            election,
            protocolPackageBinding,
            reportPackage,
            await repository.GetReportArtifactsAsync(reportPackage.Id),
            await repository.GetBoundaryArtifactsAsync(election.ElectionId),
            await repository.GetAcceptedBallotsAsync(election.ElectionId),
            await repository.GetPublishedBallotsAsync(election.ElectionId),
            finalizationSessions,
            finalizationShares,
            await repository.GetFinalizationReleaseEvidenceRecordsAsync(election.ElectionId),
            await repository.GetRosterEntriesAsync(election.ElectionId),
            await repository.GetParticipationRecordsAsync(election.ElectionId),
            VerificationPackageView.PublicAnonymous,
            VerificationProfileIds.PublicAnonymousV1,
            false,
            reportPackage.SealedAt ?? reportPackage.AttemptedAt,
            await repository.GetVoterCeremonyRecordsAsync(election.ElectionId),
            await repository.GetPreparedBallotCommitmentsAsync(election.ElectionId),
            await repository.GetSpoiledPreparedBallotsAsync(election.ElectionId),
            await repository.GetRosterImportEvidencesAsync(election.ElectionId),
            await repository.GetEligibilityPolicyEvidencesAsync(election.ElectionId),
            await repository.GetCommitmentSchemeEvidencesAsync(election.ElectionId),
            await repository.GetCommitmentRegistrationsAsync(election.ElectionId),
            await repository.GetCheckoffConsumptionsAsync(election.ElectionId),
            await repository.GetEligibilityActivationEventsAsync(election.ElectionId),
            PublicationProofTranscripts: await repository.GetPublicationProofTranscriptsAsync(election.ElectionId),
            PublicationProofSessions: await repository.GetPublicationProofSessionsAsync(election.ElectionId),
            PublicationWitnessDeletionReceipts: await repository.GetPublicationWitnessDeletionReceiptsAsync(election.ElectionId));

        var exportResult = verificationPackageExportService.Export(request);
        if (!exportResult.Success)
        {
            return ElectionReceiptPublicPackageBindingResult.Unavailable(exportResult.Code);
        }

        if (string.IsNullOrWhiteSpace(exportResult.PackageId) ||
            string.IsNullOrWhiteSpace(exportResult.PackageHash))
        {
            return ElectionReceiptPublicPackageBindingResult.Unavailable("public_package_identity_missing");
        }

        return ElectionReceiptPublicPackageBindingResult.Available(
            exportResult.PackageId,
            exportResult.PackageHash,
            VerificationProfileIds.PublicAnonymousV1);
    }
}
