using FluentAssertions;
using HushNode.Elections;
using HushNode.Elections.gRPC;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;
using Moq;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class Feat136ElectionReceiptPackageBindingResolverTests
{
    [Fact]
    public async Task ResolvePublicPackageBindingAsync_WithFinalizedSealedPackage_ExportsPublicAnonymousPackageIdentity()
    {
        var election = CreateFinalizedElection();
        var reportPackage = CreateSealedReportPackage(election);
        var protocolBinding = CreateSealedProtocolPackageBinding(election);
        var acceptedBallot = ElectionModelFactory.CreateAcceptedBallotRecord(
            election.ElectionId,
            "ciphertext-final",
            "proof-final",
            "nullifier-final",
            acceptedAt: DateTime.UtcNow.AddMinutes(-5),
            preparedBallotId: Guid.NewGuid(),
            preparedBallotHash: "prepared-final",
            receiptCommitment: "receipt-commitment-final",
            receiptCommitmentScheme: "hushvoting-sp04-receipt-commitment-sha256-v1",
            ballotDefinitionVersion: election.BallotDefinitionVersion,
            ballotDefinitionHash: election.BallotDefinitionHash);
        var repository = CreateRepository(election, reportPackage, protocolBinding, [acceptedBallot]);
        var exportService = new Mock<IElectionVerificationPackageExportService>();
        ElectionVerificationPackageExportRequest? capturedRequest = null;
        exportService
            .Setup(x => x.Export(It.IsAny<ElectionVerificationPackageExportRequest>()))
            .Callback<ElectionVerificationPackageExportRequest>(request => capturedRequest = request)
            .Returns(new ElectionVerificationPackageExportResult(
                true,
                VerificationResultCodes.PackageStructureValid,
                "Verification package exported.",
                $"HushElectionPackage-{election.ElectionId}",
                new string('b', 64),
                []));
        var sut = new ElectionReceiptPackageBindingResolver(exportService.Object);

        var result = await sut.ResolvePublicPackageBindingAsync(repository.Object, election);

        result.IsAvailable.Should().BeTrue();
        result.PackageId.Should().Be($"HushElectionPackage-{election.ElectionId}");
        result.PackageHash.Should().Be(new string('b', 64));
        result.VerifierProfileId.Should().Be(VerificationProfileIds.PublicAnonymousV1);
        result.UnavailableReason.Should().BeEmpty();

        capturedRequest.Should().NotBeNull();
        capturedRequest!.PackageView.Should().Be(VerificationPackageView.PublicAnonymous);
        capturedRequest.VerifierProfileId.Should().Be(VerificationProfileIds.PublicAnonymousV1);
        capturedRequest.RestrictedAccessAuthorized.Should().BeFalse();
        capturedRequest.ReportPackage.Should().Be(reportPackage);
        capturedRequest.ProtocolPackageBinding.Should().Be(protocolBinding);
        capturedRequest.AcceptedBallots.Should().ContainSingle(x =>
            x.ReceiptCommitment == acceptedBallot.ReceiptCommitment &&
            x.PreparedBallotHash == acceptedBallot.PreparedBallotHash);
    }

    [Fact]
    public async Task ResolvePublicPackageBindingAsync_WithMissingSealedReportPackage_ReturnsUnavailableWithoutExport()
    {
        var election = CreateFinalizedElection();
        var repository = CreateRepository(
            election,
            reportPackage: null,
            protocolBinding: CreateSealedProtocolPackageBinding(election),
            acceptedBallots: []);
        var exportService = new Mock<IElectionVerificationPackageExportService>();
        var sut = new ElectionReceiptPackageBindingResolver(exportService.Object);

        var result = await sut.ResolvePublicPackageBindingAsync(repository.Object, election);

        result.IsAvailable.Should().BeFalse();
        result.UnavailableReason.Should().Be("public_package_missing");
        exportService.Verify(x => x.Export(It.IsAny<ElectionVerificationPackageExportRequest>()), Times.Never);
    }

    [Fact]
    public async Task ResolvePublicPackageBindingAsync_WithNonFinalizedElection_ReturnsUnavailableWithoutRepositoryLookup()
    {
        var election = CreateFinalizedElection() with
        {
            LifecycleState = ElectionLifecycleState.Open,
            FinalizedAt = null,
        };
        var repository = new Mock<IElectionsRepository>();
        var exportService = new Mock<IElectionVerificationPackageExportService>();
        var sut = new ElectionReceiptPackageBindingResolver(exportService.Object);

        var result = await sut.ResolvePublicPackageBindingAsync(repository.Object, election);

        result.IsAvailable.Should().BeFalse();
        result.UnavailableReason.Should().Be("election_not_finalized");
        repository.VerifyNoOtherCalls();
        exportService.Verify(x => x.Export(It.IsAny<ElectionVerificationPackageExportRequest>()), Times.Never);
    }

    private static Mock<IElectionsRepository> CreateRepository(
        ElectionRecord election,
        ElectionReportPackageRecord? reportPackage,
        ProtocolPackageBindingRecord? protocolBinding,
        IReadOnlyList<ElectionAcceptedBallotRecord> acceptedBallots)
    {
        var repository = new Mock<IElectionsRepository>();
        repository.Setup(x => x.GetLatestReportPackageAsync(election.ElectionId)).ReturnsAsync(reportPackage);
        repository.Setup(x => x.GetSealedProtocolPackageBindingAsync(election.ElectionId)).ReturnsAsync(protocolBinding);
        repository.Setup(x => x.GetLatestProtocolPackageBindingAsync(election.ElectionId)).ReturnsAsync(protocolBinding);
        repository.Setup(x => x.GetReportArtifactsAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Array.Empty<ElectionReportArtifactRecord>());
        repository.Setup(x => x.GetBoundaryArtifactsAsync(election.ElectionId))
            .ReturnsAsync(Array.Empty<ElectionBoundaryArtifactRecord>());
        repository.Setup(x => x.GetAcceptedBallotsAsync(election.ElectionId)).ReturnsAsync(acceptedBallots);
        repository.Setup(x => x.GetPublishedBallotsAsync(election.ElectionId))
            .ReturnsAsync(Array.Empty<ElectionPublishedBallotRecord>());
        repository.Setup(x => x.GetFinalizationSessionsAsync(election.ElectionId))
            .ReturnsAsync(Array.Empty<ElectionFinalizationSessionRecord>());
        repository.Setup(x => x.GetFinalizationReleaseEvidenceRecordsAsync(election.ElectionId))
            .ReturnsAsync(Array.Empty<ElectionFinalizationReleaseEvidenceRecord>());
        repository.Setup(x => x.GetRosterEntriesAsync(election.ElectionId))
            .ReturnsAsync(Array.Empty<ElectionRosterEntryRecord>());
        repository.Setup(x => x.GetParticipationRecordsAsync(election.ElectionId))
            .ReturnsAsync(Array.Empty<ElectionParticipationRecord>());
        repository.Setup(x => x.GetVoterCeremonyRecordsAsync(election.ElectionId))
            .ReturnsAsync(Array.Empty<ElectionVoterCeremonyRecord>());
        repository.Setup(x => x.GetPreparedBallotCommitmentsAsync(election.ElectionId))
            .ReturnsAsync(Array.Empty<ElectionPreparedBallotCommitmentRecord>());
        repository.Setup(x => x.GetSpoiledPreparedBallotsAsync(election.ElectionId))
            .ReturnsAsync(Array.Empty<ElectionSpoiledPreparedBallotRecord>());
        repository.Setup(x => x.GetRosterImportEvidencesAsync(election.ElectionId))
            .ReturnsAsync(Array.Empty<ElectionRosterImportEvidenceRecord>());
        repository.Setup(x => x.GetEligibilityPolicyEvidencesAsync(election.ElectionId))
            .ReturnsAsync(Array.Empty<ElectionEligibilityPolicyEvidenceRecord>());
        repository.Setup(x => x.GetCommitmentSchemeEvidencesAsync(election.ElectionId))
            .ReturnsAsync(Array.Empty<ElectionCommitmentSchemeEvidenceRecord>());
        repository.Setup(x => x.GetCommitmentRegistrationsAsync(election.ElectionId))
            .ReturnsAsync(Array.Empty<ElectionCommitmentRegistrationRecord>());
        repository.Setup(x => x.GetCheckoffConsumptionsAsync(election.ElectionId))
            .ReturnsAsync(Array.Empty<ElectionCheckoffConsumptionRecord>());
        repository.Setup(x => x.GetEligibilityActivationEventsAsync(election.ElectionId))
            .ReturnsAsync(Array.Empty<ElectionEligibilityActivationEventRecord>());
        repository.Setup(x => x.GetPublicationProofTranscriptsAsync(election.ElectionId))
            .ReturnsAsync(Array.Empty<ElectionPublicationProofTranscriptRecord>());
        repository.Setup(x => x.GetPublicationProofSessionsAsync(election.ElectionId))
            .ReturnsAsync(Array.Empty<ElectionPublicationProofSessionRecord>());
        repository.Setup(x => x.GetPublicationWitnessDeletionReceiptsAsync(election.ElectionId))
            .ReturnsAsync(Array.Empty<ElectionPublicationWitnessDeletionReceiptRecord>());
        return repository;
    }

    private static ElectionRecord CreateFinalizedElection() =>
        ElectionModelFactory.CreateDraftRecord(
            ElectionId.NewElectionId,
            "Board Election",
            "Annual board vote",
            "owner-address",
            null,
            ElectionClass.OrganizationalRemoteVoting,
            ElectionBindingStatus.Binding,
            ElectionGovernanceMode.AdminOnly,
            ElectionDisclosureMode.FinalResultsOnly,
            ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            VoteUpdatePolicy.SingleSubmissionOnly,
            EligibilitySourceType.OrganizationImportedRoster,
            EligibilityMutationPolicy.FrozenAtOpen,
            CreateSingleWinnerRule(),
            [new ApprovedClientApplicationRecord("hushvoting", "1.0.0")],
            "omega-v1.0.0",
            ReportingPolicy.DefaultPhaseOnePackage,
            ReviewWindowPolicy.NoReviewWindow,
            [
                new ElectionOptionDefinition("alice", "Alice", null, 10, IsBlankOption: false),
                new ElectionOptionDefinition("bob", "Bob", null, 20, IsBlankOption: false),
            ]) with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            FinalizedAt = DateTime.UtcNow.AddMinutes(-1),
            TallyReadyArtifactId = Guid.NewGuid(),
            UnofficialResultArtifactId = Guid.NewGuid(),
            OfficialResultArtifactId = Guid.NewGuid(),
            FinalizeArtifactId = Guid.NewGuid(),
            BallotDefinitionVersion = 1,
            BallotDefinitionHash = [1, 2, 3, 4],
            BallotDefinitionSealedAt = DateTime.UtcNow.AddHours(-1),
        };

    private static ElectionReportPackageRecord CreateSealedReportPackage(ElectionRecord election) =>
        ElectionModelFactory.CreateSealedReportPackage(
            election.ElectionId,
            attemptNumber: 1,
            tallyReadyArtifactId: election.TallyReadyArtifactId ?? Guid.NewGuid(),
            unofficialResultArtifactId: election.UnofficialResultArtifactId ?? Guid.NewGuid(),
            officialResultArtifactId: election.OfficialResultArtifactId ?? Guid.NewGuid(),
            finalizeArtifactId: election.FinalizeArtifactId ?? Guid.NewGuid(),
            frozenEvidenceHash: [1, 2, 3],
            frozenEvidenceFingerprint: "verification-fingerprint",
            packageHash: [4, 5, 6],
            artifactCount: 1,
            attemptedByPublicAddress: "owner-address",
            attemptedAt: DateTime.UtcNow.AddMinutes(-2),
            sealedAt: DateTime.UtcNow.AddMinutes(-1));

    private static ProtocolPackageBindingRecord CreateSealedProtocolPackageBinding(ElectionRecord election)
    {
        var catalogEntry = ElectionModelFactory.CreateApprovedProtocolPackageCatalogEntry(
            packageId: "omega-hushvoting-v1",
            packageVersion: "v1.0.0",
            specPackageHash: Hash('a'),
            proofPackageHash: Hash('b'),
            releaseManifestHash: Hash('c'),
            compatibleProfileIds: [election.SelectedProfileId],
            approvalStatus: ProtocolPackageApprovalStatus.ApprovedInternal,
            isLatestForCompatibleProfiles: true,
            specAccessLocations: [CreateProtocolPackageAccessLocation(Hash('d'))],
            proofAccessLocations: [CreateProtocolPackageAccessLocation(Hash('e'))],
            approvedAt: DateTime.UtcNow.AddMinutes(-10));

        return ElectionModelFactory.CreateProtocolPackageBindingFromCatalog(
                election.ElectionId,
                catalogEntry,
                election.SelectedProfileId,
                election.CurrentDraftRevision,
                "owner-address")
            .SealAtOpen(DateTime.UtcNow.AddMinutes(-9), "owner-address");
    }

    private static ProtocolPackageAccessLocationRecord CreateProtocolPackageAccessLocation(string contentHash) =>
        ElectionModelFactory.CreateProtocolPackageAccessLocation(
            ProtocolPackageAccessLocationKind.PublicWebsite,
            "HushNetwork public protocol package",
            "https://www.hushnetwork.social/protocol-omega/hushvoting-v1/v1.0.0/package.zip",
            contentHash);

    private static OutcomeRuleDefinition CreateSingleWinnerRule() =>
        new(
            OutcomeRuleKind.SingleWinner,
            "single_winner",
            SeatCount: 1,
            BlankVoteCountsForTurnout: true,
            BlankVoteExcludedFromWinnerSelection: true,
            BlankVoteExcludedFromThresholdDenominator: false,
            TieResolutionRule: "tie_unresolved",
            CalculationBasis: "highest_non_blank_votes");

    private static string Hash(char value) => new(char.ToLowerInvariant(value), 64);
}
