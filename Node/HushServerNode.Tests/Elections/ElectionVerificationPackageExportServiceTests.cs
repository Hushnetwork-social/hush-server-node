using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionVerificationPackageExportServiceTests
{
    [Fact]
    public void Export_PublicPackage_ShouldWriteRootFilesAndBindManifestEntries()
    {
        var result = Export(CreateRequest(VerificationPackageView.PublicAnonymous));

        result.Success.Should().BeTrue();
        result.Files.Select(x => x.RelativePath).Should().Contain(VerificationPackageFileNames.RootFiles);
        result.Files.Should().NotContain(x => x.RelativePath.StartsWith("artifacts/restricted/", StringComparison.OrdinalIgnoreCase));

        var manifest = ReadFile<AuditPackageManifestRecord>(result, VerificationPackageFileNames.AuditPackageManifest);
        foreach (var entry in manifest.Entries)
        {
            var file = result.Files.Single(x => x.RelativePath == entry.Path);
            VerificationCanonicalHash.ComputeManifestFileSha256(file.Content).Should().Be(entry.Sha256Hash);
        }
    }

    [Fact]
    public void Export_RestrictedPackageWithoutAuthorization_ShouldFailDeterministically()
    {
        var result = Export(CreateRequest(
            VerificationPackageView.RestrictedOwnerAuditor,
            restrictedAccessAuthorized: false));

        result.Success.Should().BeFalse();
        result.Code.Should().Be(VerificationResultCodes.RestrictedExportUnauthorized);
        result.Files.Should().BeEmpty();
    }

    [Fact]
    public void Export_RestrictedPackageWithAuthorization_ShouldIsolateRestrictedEvidence()
    {
        var result = Export(CreateRequest(
            VerificationPackageView.RestrictedOwnerAuditor,
            restrictedAccessAuthorized: true,
            profileId: VerificationProfileIds.RestrictedOwnerAuditorV1));

        result.Success.Should().BeTrue();
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedRosterCheckoff &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
    }

    [Fact]
    public void Export_BeforeFinalization_ShouldFailWithoutMutatingPackageFiles()
    {
        var request = CreateRequest(VerificationPackageView.PublicAnonymous);
        request = request with
        {
            Election = request.Election with
            {
                LifecycleState = ElectionLifecycleState.Closed,
            },
        };

        var result = Export(request);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(VerificationResultCodes.ElectionNotFinalized);
        result.Files.Should().BeEmpty();
    }

    private static ElectionVerificationPackageExportResult Export(
        ElectionVerificationPackageExportRequest request) =>
        new ElectionVerificationPackageExportService().Export(request);

    private static T ReadFile<T>(ElectionVerificationPackageExportResult result, string path)
    {
        var file = result.Files.Single(x => x.RelativePath == path);
        return JsonSerializer.Deserialize<T>(file.Content, VerificationJson.Options)!;
    }

    internal static ElectionVerificationPackageExportRequest CreateRequest(
        VerificationPackageView view,
        bool restrictedAccessAuthorized = false,
        string profileId = VerificationProfileIds.DevelopmentCurrentV1)
    {
        var electionId = ElectionId.NewElectionId;
        var draftElection = ElectionModelFactory.CreateDraftRecord(
            electionId,
            title: "Verifier package election",
            shortDescription: "FEAT-113 test",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "FEAT-113",
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
            governanceMode: ElectionGovernanceMode.AdminOnly,
            disclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            participationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            voteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            eligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            eligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            outcomeRule: CreatePassFailRule(),
            approvedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushvoting", "1.0.0"),
            ],
            protocolOmegaVersion: "omega-v1.1.1",
            reportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            reviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
            ownerOptions:
            [
                new ElectionOptionDefinition("yes", "Yes", "Approve", 1, false),
                new ElectionOptionDefinition("no", "No", "Reject", 2, false),
            ],
            officialResultVisibilityPolicy: OfficialResultVisibilityPolicy.PublicPlaintext);

        var acceptedBallots = new[]
        {
            ElectionModelFactory.CreateAcceptedBallotRecord(electionId, "ballot-a", "proof-a", "nullifier-a"),
            ElectionModelFactory.CreateAcceptedBallotRecord(electionId, "ballot-b", "proof-b", "nullifier-b"),
        };
        var publishedBallots = new[]
        {
            ElectionModelFactory.CreatePublishedBallotRecord(electionId, 1, "published-a", "proof-a"),
            ElectionModelFactory.CreatePublishedBallotRecord(electionId, 2, "published-b", "proof-b"),
        };
        var closeArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Close,
            draftElection,
            recordedByPublicAddress: "owner-address",
            acceptedBallotCount: acceptedBallots.Length,
            acceptedBallotSetHash: VerificationCanonicalHash.ComputeAcceptedBallotInventoryHash(acceptedBallots),
            publishedBallotCount: publishedBallots.Length,
            publishedBallotStreamHash: VerificationCanonicalHash.ComputePublishedBallotStreamHash(publishedBallots),
            finalEncryptedTallyHash: HashBytes("tally"));
        var tallyReadyArtifactId = Guid.NewGuid();
        var officialResultArtifactId = Guid.NewGuid();
        var unofficialResultArtifactId = Guid.NewGuid();
        var finalizeArtifactId = Guid.NewGuid();
        var finalizedElection = draftElection with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            ClosedAt = DateTime.UtcNow.AddMinutes(-5),
            FinalizedAt = DateTime.UtcNow,
            CloseArtifactId = closeArtifact.Id,
            TallyReadyArtifactId = tallyReadyArtifactId,
            OfficialResultArtifactId = officialResultArtifactId,
            UnofficialResultArtifactId = unofficialResultArtifactId,
            FinalizeArtifactId = finalizeArtifactId,
        };
        var binding = CreateSealedProtocolBinding(electionId, profileId);
        var reportPackage = ElectionModelFactory.CreateSealedReportPackage(
            electionId,
            attemptNumber: 1,
            tallyReadyArtifactId,
            unofficialResultArtifactId,
            officialResultArtifactId,
            finalizeArtifactId,
            frozenEvidenceHash: HashBytes("frozen"),
            frozenEvidenceFingerprint: "sha256:frozen",
            packageHash: HashBytes("report-package"),
            artifactCount: 1,
            attemptedByPublicAddress: "owner-address",
            closeBoundaryArtifactId: closeArtifact.Id);
        var reportArtifact = ElectionModelFactory.CreateReportArtifact(
            reportPackage.Id,
            electionId,
            ElectionReportArtifactKind.MachineManifest,
            ElectionReportArtifactFormat.Json,
            ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
            sortOrder: 1,
            title: "Machine manifest",
            fileName: "canonical-manifest.json",
            mediaType: "application/json",
            contentHash: HashBytes("{\"ok\":true}"),
            content: "{\"ok\":true}");

        return new ElectionVerificationPackageExportRequest(
            finalizedElection,
            binding,
            reportPackage,
            [reportArtifact],
            [closeArtifact],
            acceptedBallots,
            publishedBallots,
            FinalizationSessions: [],
            FinalizationShares: [],
            ReleaseEvidenceRecords: [],
            RosterEntries:
            [
                CreateRosterEntry(electionId),
            ],
            ParticipationRecords:
            [
                new ElectionParticipationRecord(
                    electionId,
                    "voter-1",
                    ElectionParticipationStatus.CountedAsVoted,
                    DateTime.UtcNow,
                    DateTime.UtcNow,
                    LatestTransactionId: null,
                    LatestBlockHeight: null,
                    LatestBlockId: null),
            ],
            view,
            profileId,
            restrictedAccessAuthorized,
            ExportedAt: DateTime.UnixEpoch);
    }

    private static ProtocolPackageBindingRecord CreateSealedProtocolBinding(
        ElectionId electionId,
        string profileId)
    {
        var accessLocation = ElectionModelFactory.CreateProtocolPackageAccessLocation(
            ProtocolPackageAccessLocationKind.Repository,
            "Repository",
            "https://example.test/protocol",
            HashHex("access"));
        var catalogEntry = ElectionModelFactory.CreateApprovedProtocolPackageCatalogEntry(
            "omega-hushvoting-v1",
            "v1.1.1",
            HashHex("spec"),
            HashHex("proof"),
            HashHex("release"),
            [profileId],
            ProtocolPackageApprovalStatus.DraftPrivate,
            isLatestForCompatibleProfiles: true,
            [accessLocation],
            [accessLocation]);

        return ElectionModelFactory.CreateProtocolPackageBindingFromCatalog(
                electionId,
                catalogEntry,
                profileId,
                draftRevision: 1,
                boundByPublicAddress: "owner-address")
            .SealAtOpen(DateTime.UtcNow, "owner-address");
    }

    private static ElectionRosterEntryRecord CreateRosterEntry(ElectionId electionId) =>
        new(
            electionId,
            "voter-1",
            ElectionRosterContactType.Email,
            "voter@example.test",
            ElectionVoterLinkStatus.Linked,
            "actor-voter-1",
            DateTime.UtcNow,
            ElectionVotingRightStatus.Active,
            DateTime.UtcNow,
            WasPresentAtOpen: true,
            WasActiveAtOpen: true,
            LastActivatedAt: DateTime.UtcNow,
            LastActivatedByPublicAddress: "owner-address",
            LastUpdatedAt: DateTime.UtcNow,
            LatestTransactionId: null,
            LatestBlockHeight: null,
            LatestBlockId: null);

    private static OutcomeRuleDefinition CreatePassFailRule() =>
        new(
            OutcomeRuleKind.PassFail,
            TemplateKey: "pass-fail-simple-majority",
            SeatCount: 1,
            BlankVoteCountsForTurnout: true,
            BlankVoteExcludedFromWinnerSelection: true,
            BlankVoteExcludedFromThresholdDenominator: true,
            TieResolutionRule: "reject-on-tie",
            CalculationBasis: "counted-votes");

    private static byte[] HashBytes(string value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static string HashHex(string value) =>
        Convert.ToHexString(HashBytes(value)).ToLowerInvariant();
}
