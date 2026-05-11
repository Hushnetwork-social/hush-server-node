using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionReportPackageServiceTests
{
    [Fact]
    public void Build_WithAdminOnlyBindingElection_EmitsProtectedCustodyTruthInHumanArtifacts()
    {
        var service = new ElectionReportPackageService();
        var electionId = ElectionId.NewElectionId;
        var draftElection = ElectionModelFactory.CreateDraftRecord(
            electionId,
            title: "Admin-only protected custody election",
            shortDescription: "FEAT-105 report package unit test",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "FEAT-105-ADMIN",
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
                new ApprovedClientApplicationRecord("hushsocial", "1.0.0"),
            ],
            protocolOmegaVersion: "omega-v1.0.0",
            reportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            reviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
            ownerOptions:
            [
                new ElectionOptionDefinition("yes", "Yes", "Approve the proposal", 1, false),
                new ElectionOptionDefinition("no", "No", "Reject the proposal", 2, false),
            ],
            officialResultVisibilityPolicy: OfficialResultVisibilityPolicy.PublicPlaintext);

        var acceptedBallotSetHash = new byte[] { 1, 2, 3 };
        var publishedBallotStreamHash = new byte[] { 4, 5, 6 };
        var finalEncryptedTallyHash = new byte[] { 7, 8, 9 };
        var activeDenominatorSetHash = new byte[] { 10, 11, 12 };
        var closeArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Close,
            draftElection,
            recordedByPublicAddress: "owner-address",
            frozenEligibleVoterSetHash: new byte[] { 13, 14, 15 },
            acceptedBallotCount: 1,
            acceptedBallotSetHash: acceptedBallotSetHash,
            publishedBallotCount: 1,
            publishedBallotStreamHash: publishedBallotStreamHash,
            finalEncryptedTallyHash: finalEncryptedTallyHash);
        var tallyReadyArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.TallyReady,
            draftElection,
            recordedByPublicAddress: "owner-address",
            acceptedBallotCount: 1,
            acceptedBallotSetHash: acceptedBallotSetHash,
            publishedBallotCount: 1,
            publishedBallotStreamHash: publishedBallotStreamHash,
            finalEncryptedTallyHash: finalEncryptedTallyHash);
        var finalizeArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Finalize,
            draftElection,
            recordedByPublicAddress: "owner-address",
            acceptedBallotCount: 1,
            acceptedBallotSetHash: acceptedBallotSetHash,
            publishedBallotCount: 1,
            publishedBallotStreamHash: publishedBallotStreamHash,
            finalEncryptedTallyHash: finalEncryptedTallyHash);

        var finalizedElection = draftElection with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            ClosedAt = DateTime.UtcNow.AddMinutes(-2),
            TallyReadyAt = DateTime.UtcNow.AddMinutes(-1),
            FinalizedAt = DateTime.UtcNow,
            CloseArtifactId = closeArtifact.Id,
            TallyReadyArtifactId = tallyReadyArtifact.Id,
            FinalizeArtifactId = finalizeArtifact.Id,
        };

        var denominatorEvidence = new ElectionResultDenominatorEvidence(
            ElectionEligibilitySnapshotType.Close,
            EligibilitySnapshotId: null,
            BoundaryArtifactId: closeArtifact.Id,
            ActiveDenominatorSetHash: activeDenominatorSetHash);
        var unofficialResult = ElectionModelFactory.CreateResultArtifact(
            electionId,
            ElectionResultArtifactKind.Unofficial,
            ElectionResultArtifactVisibility.PublicPlaintext,
            title: "Unofficial result",
            namedOptionResults:
            [
                new ElectionResultOptionCount("yes", "Yes", "Approve the proposal", 1, 1, 1),
                new ElectionResultOptionCount("no", "No", "Reject the proposal", 2, 2, 0),
            ],
            blankCount: 0,
            totalVotedCount: 1,
            eligibleToVoteCount: 1,
            didNotVoteCount: 0,
            denominatorEvidence,
            recordedByPublicAddress: "owner-address",
            tallyReadyArtifactId: tallyReadyArtifact.Id,
            publicPayload: "{\"mode\":\"binding\"}");
        var officialResult = ElectionModelFactory.CreateResultArtifact(
            electionId,
            ElectionResultArtifactKind.Official,
            ElectionResultArtifactVisibility.PublicPlaintext,
            title: "Official result",
            namedOptionResults: unofficialResult.NamedOptionResults,
            blankCount: unofficialResult.BlankCount,
            totalVotedCount: unofficialResult.TotalVotedCount,
            eligibleToVoteCount: unofficialResult.EligibleToVoteCount,
            didNotVoteCount: unofficialResult.DidNotVoteCount,
            denominatorEvidence,
            recordedByPublicAddress: "owner-address",
            tallyReadyArtifactId: tallyReadyArtifact.Id,
            sourceResultArtifactId: unofficialResult.Id,
            publicPayload: "{\"mode\":\"binding\"}");

        finalizedElection = finalizedElection with
        {
            UnofficialResultArtifactId = unofficialResult.Id,
            OfficialResultArtifactId = officialResult.Id,
        };

        var buildResult = service.Build(new ElectionReportPackageBuildRequest(
            finalizedElection,
            closeArtifact,
            tallyReadyArtifact,
            finalizeArtifact,
            unofficialResult,
            officialResult,
            CloseEligibilitySnapshot: null,
            ProtocolPackageBinding: null,
            FinalizationSession: null,
            FinalizationReleaseEvidence: null,
            FinalizationGovernedProposal: null,
            FinalizationGovernedApprovals: Array.Empty<ElectionGovernedProposalApprovalRecord>(),
            FinalizationShares: Array.Empty<ElectionFinalizationShareRecord>(),
            WarningAcknowledgements: Array.Empty<ElectionWarningAcknowledgementRecord>(),
            TrusteeInvitations: Array.Empty<ElectionTrusteeInvitationRecord>(),
            RosterEntries:
            [
                ElectionModelFactory.CreateRosterEntry(
                    electionId,
                    "voter-alice",
                    ElectionRosterContactType.Email,
                    "alice@hush.test"),
            ],
            ParticipationRecords:
            [
                ElectionModelFactory.CreateParticipationRecord(
                    electionId,
                    "voter-alice",
                    ElectionParticipationStatus.CountedAsVoted),
            ],
            AttemptNumber: 1,
            PreviousAttemptId: null,
            AttemptedByPublicAddress: "owner-address",
            AttemptedAt: DateTime.UtcNow));

        buildResult.IsSuccess.Should().BeTrue();

        var humanManifest = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanManifest);
        humanManifest.Content.Should().Contain("AdminOnly");
        humanManifest.Content.Should().Contain("admin-prod-1of1");
        humanManifest.Content.Should().Contain("production-like ceremony profiles");
        humanManifest.Content.Should().Contain("owner-admin protected custody profile");

        var humanAudit = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanAuditProvenanceReport);
        humanAudit.Content.Should().Contain("AdminOnly");
        humanAudit.Content.Should().Contain("admin-prod-1of1");
        humanAudit.Content.Should().Contain("owner-admin protected custody profile");
        humanAudit.Content.Should().Contain("single-ballot inspection authority");

        var humanResult = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanResultReport);
        humanResult.Content.Should().Contain("production-like ceremony profiles");
        humanResult.Content.Should().Contain("admin-prod-1of1");
        humanResult.Content.Should().Contain("protected-ballot path");
        humanResult.Content.Should().Contain("Non-binding election: `no`");
    }

    [Fact]
    public void Build_WithAdminOnlyNonBindingProtectedElection_DoesNotMislabelItAsOpenAudit()
    {
        var service = new ElectionReportPackageService();
        var electionId = ElectionId.NewElectionId;
        var draftElection = ElectionModelFactory.CreateDraftRecord(
            electionId,
            title: "Admin-only non-binding protected election",
            shortDescription: "FEAT-105 report truth regression",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "FEAT-105-ADMIN-NONBINDING",
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.NonBinding,
            selectedProfileId: "admin-prod-1of1",
            selectedProfileDevOnly: false,
            governanceMode: ElectionGovernanceMode.AdminOnly,
            disclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            participationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            voteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            eligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            eligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            outcomeRule: CreatePassFailRule(),
            approvedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushsocial", "1.0.0"),
            ],
            protocolOmegaVersion: "omega-v1.0.0",
            reportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            reviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
            ownerOptions:
            [
                new ElectionOptionDefinition("yes", "Yes", "Approve the proposal", 1, false),
                new ElectionOptionDefinition("no", "No", "Reject the proposal", 2, false),
            ],
            officialResultVisibilityPolicy: OfficialResultVisibilityPolicy.PublicPlaintext);

        var closeArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Close,
            draftElection,
            recordedByPublicAddress: "owner-address",
            frozenEligibleVoterSetHash: new byte[] { 1, 2, 3 },
            acceptedBallotCount: 1,
            acceptedBallotSetHash: new byte[] { 4, 5, 6 },
            publishedBallotCount: 1,
            publishedBallotStreamHash: new byte[] { 7, 8, 9 },
            finalEncryptedTallyHash: new byte[] { 10, 11, 12 });
        var tallyReadyArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.TallyReady,
            draftElection,
            recordedByPublicAddress: "owner-address",
            acceptedBallotCount: 1,
            acceptedBallotSetHash: new byte[] { 4, 5, 6 },
            publishedBallotCount: 1,
            publishedBallotStreamHash: new byte[] { 7, 8, 9 },
            finalEncryptedTallyHash: new byte[] { 10, 11, 12 });
        var finalizeArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Finalize,
            draftElection,
            recordedByPublicAddress: "owner-address",
            acceptedBallotCount: 1,
            acceptedBallotSetHash: new byte[] { 4, 5, 6 },
            publishedBallotCount: 1,
            publishedBallotStreamHash: new byte[] { 7, 8, 9 },
            finalEncryptedTallyHash: new byte[] { 10, 11, 12 });

        var finalizedElection = draftElection with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            ClosedAt = DateTime.UtcNow.AddMinutes(-2),
            TallyReadyAt = DateTime.UtcNow.AddMinutes(-1),
            FinalizedAt = DateTime.UtcNow,
            CloseArtifactId = closeArtifact.Id,
            TallyReadyArtifactId = tallyReadyArtifact.Id,
            FinalizeArtifactId = finalizeArtifact.Id,
        };

        var denominatorEvidence = new ElectionResultDenominatorEvidence(
            ElectionEligibilitySnapshotType.Close,
            EligibilitySnapshotId: null,
            BoundaryArtifactId: closeArtifact.Id,
            ActiveDenominatorSetHash: new byte[] { 13, 14, 15 });
        var unofficialResult = ElectionModelFactory.CreateResultArtifact(
            electionId,
            ElectionResultArtifactKind.Unofficial,
            ElectionResultArtifactVisibility.PublicPlaintext,
            title: "Unofficial result",
            namedOptionResults:
            [
                new ElectionResultOptionCount("yes", "Yes", "Approve the proposal", 1, 1, 1),
                new ElectionResultOptionCount("no", "No", "Reject the proposal", 2, 2, 0),
            ],
            blankCount: 0,
            totalVotedCount: 1,
            eligibleToVoteCount: 1,
            didNotVoteCount: 0,
            denominatorEvidence,
            recordedByPublicAddress: "owner-address",
            tallyReadyArtifactId: tallyReadyArtifact.Id,
            publicPayload: "{\"mode\":\"protected-nonbinding\"}");
        var officialResult = ElectionModelFactory.CreateResultArtifact(
            electionId,
            ElectionResultArtifactKind.Official,
            ElectionResultArtifactVisibility.PublicPlaintext,
            title: "Official result",
            namedOptionResults: unofficialResult.NamedOptionResults,
            blankCount: unofficialResult.BlankCount,
            totalVotedCount: unofficialResult.TotalVotedCount,
            eligibleToVoteCount: unofficialResult.EligibleToVoteCount,
            didNotVoteCount: unofficialResult.DidNotVoteCount,
            denominatorEvidence,
            recordedByPublicAddress: "owner-address",
            tallyReadyArtifactId: tallyReadyArtifact.Id,
            sourceResultArtifactId: unofficialResult.Id,
            publicPayload: "{\"mode\":\"protected-nonbinding\"}");

        finalizedElection = finalizedElection with
        {
            UnofficialResultArtifactId = unofficialResult.Id,
            OfficialResultArtifactId = officialResult.Id,
        };

        var buildResult = service.Build(new ElectionReportPackageBuildRequest(
            finalizedElection,
            closeArtifact,
            tallyReadyArtifact,
            finalizeArtifact,
            unofficialResult,
            officialResult,
            CloseEligibilitySnapshot: null,
            ProtocolPackageBinding: null,
            FinalizationSession: null,
            FinalizationReleaseEvidence: null,
            FinalizationGovernedProposal: null,
            FinalizationGovernedApprovals: Array.Empty<ElectionGovernedProposalApprovalRecord>(),
            FinalizationShares: Array.Empty<ElectionFinalizationShareRecord>(),
            WarningAcknowledgements: Array.Empty<ElectionWarningAcknowledgementRecord>(),
            TrusteeInvitations: Array.Empty<ElectionTrusteeInvitationRecord>(),
            RosterEntries:
            [
                ElectionModelFactory.CreateRosterEntry(
                    electionId,
                    "voter-alice",
                    ElectionRosterContactType.Email,
                    "alice@hush.test"),
            ],
            ParticipationRecords:
            [
                ElectionModelFactory.CreateParticipationRecord(
                    electionId,
                    "voter-alice",
                    ElectionParticipationStatus.CountedAsVoted),
            ],
            AttemptNumber: 1,
            PreviousAttemptId: null,
            AttemptedByPublicAddress: "owner-address",
            AttemptedAt: DateTime.UtcNow));

        buildResult.IsSuccess.Should().BeTrue();

        var humanResult = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanResultReport);
        humanResult.Content.Should().Contain("Binding status: `NonBinding`");
        humanResult.Content.Should().Contain("Non-binding election: `yes`");
        humanResult.Content.Should().Contain("Selected circuit/profile: `admin-prod-1of1`");
        humanResult.Content.Should().Contain("Circuit class: `Production`");
        humanResult.Content.Should().Contain("production-like ceremony profiles");
        humanResult.Content.Should().Contain("protected-ballot path");
        humanResult.Content.Should().NotContain("open-audit path");

        var humanManifest = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanManifest);
        humanManifest.Content.Should().Contain("Binding status: `NonBinding`");
        humanManifest.Content.Should().Contain("Non-binding election: `yes`");
        humanManifest.Content.Should().Contain("Selected circuit/profile: `admin-prod-1of1`");
        humanManifest.Content.Should().Contain("Circuit class: `Production`");

        var humanAudit = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanAuditProvenanceReport);
        humanAudit.Content.Should().Contain("Binding status: `NonBinding`");
        humanAudit.Content.Should().Contain("Non-binding election: `yes`");
        humanAudit.Content.Should().Contain("Selected circuit/profile: `admin-prod-1of1`");
        humanAudit.Content.Should().Contain("Circuit class: `Production`");

        var humanRoster = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanNamedParticipationRoster);
        humanRoster.Content.Should().Contain("Binding status: `NonBinding`");
        humanRoster.Content.Should().Contain("Non-binding election: `yes`");
        humanRoster.Content.Should().Contain("Selected circuit/profile: `admin-prod-1of1`");
        humanRoster.Content.Should().Contain("Circuit class: `Production`");

        var humanOutcome = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanOutcomeDetermination);
        humanOutcome.Content.Should().Contain("Binding status: `NonBinding`");
        humanOutcome.Content.Should().Contain("Non-binding election: `yes`");
        humanOutcome.Content.Should().Contain("Selected circuit/profile: `admin-prod-1of1`");
        humanOutcome.Content.Should().Contain("Circuit class: `Production`");

        var humanDispute = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanDisputeReviewIndex);
        humanDispute.Content.Should().Contain("Binding status: `NonBinding`");
        humanDispute.Content.Should().Contain("Non-binding election: `yes`");
        humanDispute.Content.Should().Contain("Selected circuit/profile: `admin-prod-1of1`");
        humanDispute.Content.Should().Contain("Circuit class: `Production`");

        var machineRoster = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.MachineNamedParticipationRosterProjection);
        machineRoster.Content.Should().Contain("\"bindingStatus\": \"NonBinding\"");
        machineRoster.Content.Should().Contain("\"isNonBindingElection\": true");
        machineRoster.Content.Should().Contain("\"selectedProfileId\": \"admin-prod-1of1\"");
        machineRoster.Content.Should().Contain("\"circuitClassification\": \"Production\"");

        var machineOutcome = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.MachineOutcomeDeterminationProjection);
        machineOutcome.Content.Should().Contain("\"bindingStatus\": \"NonBinding\"");
        machineOutcome.Content.Should().Contain("\"isNonBindingElection\": true");
        machineOutcome.Content.Should().Contain("\"selectedProfileId\": \"admin-prod-1of1\"");
        machineOutcome.Content.Should().Contain("\"circuitClassification\": \"Production\"");

        var machineDispute = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.MachineDisputeReviewIndexProjection);
        machineDispute.Content.Should().Contain("\"bindingStatus\": \"NonBinding\"");
        machineDispute.Content.Should().Contain("\"isNonBindingElection\": true");
        machineDispute.Content.Should().Contain("\"selectedProfileId\": \"admin-prod-1of1\"");
        machineDispute.Content.Should().Contain("\"circuitClassification\": \"Production\"");

        var machineEvidenceGraph = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.MachineEvidenceGraph);
        machineEvidenceGraph.Content.Should().Contain("\"bindingStatus\": \"NonBinding\"");
        machineEvidenceGraph.Content.Should().Contain("\"isNonBindingElection\": true");
        machineEvidenceGraph.Content.Should().Contain("\"selectedProfileId\": \"admin-prod-1of1\"");
        machineEvidenceGraph.Content.Should().Contain("\"circuitClassification\": \"Production\"");
    }

    [Fact]
    public void Build_WithSealedProtocolPackageBinding_EmitsRefsAndAccessLocationsInAuditArtifacts()
    {
        var service = new ElectionReportPackageService();
        var election = CreateFinalizedElectionForReportPackage();
        var binding = CreateSealedProtocolPackageBinding(election);
        var request = CreateReportBuildRequest(election, binding);

        var buildResult = service.Build(request);
        var buildWithoutBinding = service.Build(request with { ProtocolPackageBinding = null });

        buildResult.IsSuccess.Should().BeTrue();
        buildWithoutBinding.IsSuccess.Should().BeTrue();
        buildResult.Package.FrozenEvidenceHash.Should().NotEqual(buildWithoutBinding.Package.FrozenEvidenceHash);

        var machineManifest = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.MachineManifest);
        machineManifest.Content.Should().Contain("\"protocolPackageBinding\"");
        machineManifest.Content.Should().Contain("\"packageVersion\": \"v1.0.0\"");
        machineManifest.Content.Should().Contain($"\"specPackageHash\": \"{Hash('a')}\"");
        machineManifest.Content.Should().Contain($"\"proofPackageHash\": \"{Hash('b')}\"");
        machineManifest.Content.Should().Contain($"\"releaseManifestHash\": \"{Hash('c')}\"");
        machineManifest.Content.Should().Contain("\"externalReviewAvailability\": \"not_available\"");
        machineManifest.Content.Should().Contain("\"externalReviewClaimState\": \"program_defined\"");
        machineManifest.Content.Should().Contain(
            "\"externalReviewCustomerSafeSummary\": \"External examination program is defined; no reviewer conclusion is available.\"");
        machineManifest.Content.Should().Contain("https://www.hushnetwork.social/protocol-omega/hushvoting-v1/spec.zip");

        var machineEvidenceGraph = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.MachineEvidenceGraph);
        machineEvidenceGraph.Content.Should().Contain("\"protocolPackageBinding\"");
        machineEvidenceGraph.Content.Should().Contain($"\"releaseManifestHash\": \"{Hash('c')}\"");

        var machineAudit = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.MachineAuditProvenanceReportProjection);
        machineAudit.Content.Should().Contain("\"protocolPackageBinding\"");
        machineAudit.Content.Should().Contain("Temporary access-location outage is operational");

        var humanManifest = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanManifest);
        humanManifest.Content.Should().Contain("Protocol package binding id");
        humanManifest.Content.Should().Contain($"Spec package hash: `{Hash('a')}`");
        humanManifest.Content.Should().Contain("Spec access locations: `1`");
        humanManifest.Content.Should().Contain(
            "External review summary: External examination program is defined; no reviewer conclusion is available.");

        var humanAudit = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanAuditProvenanceReport);
        humanAudit.Content.Should().Contain("## Protocol Omega Package Binding");
        humanAudit.Content.Should().Contain("Access-location note: Protocol package archives are referenced by immutable hashes");
        humanAudit.Content.Should().Contain("Website spec package");
        humanAudit.Content.Should().Contain("Website proof package");
        ElectionSp09ProfileIds.ForbiddenClaimPhrases.Should().AllSatisfy(phrase =>
            humanAudit.Content.Contains(phrase, StringComparison.OrdinalIgnoreCase).Should().BeFalse());
    }

    private static OutcomeRuleDefinition CreatePassFailRule() =>
        new(
            OutcomeRuleKind.PassFail,
            "pass_fail_yes_no",
            SeatCount: 1,
            BlankVoteCountsForTurnout: true,
            BlankVoteExcludedFromWinnerSelection: true,
            BlankVoteExcludedFromThresholdDenominator: true,
            TieResolutionRule: "tie_unresolved",
            CalculationBasis: "simple_majority_of_non_blank_votes");

    private static ElectionRecord CreateFinalizedElectionForReportPackage()
    {
        var electionId = ElectionId.NewElectionId;
        var draftElection = ElectionModelFactory.CreateDraftRecord(
            electionId,
            title: "Protocol package report election",
            shortDescription: "FEAT-112 report package refs",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "FEAT-112-REPORT",
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
            selectedProfileId: ElectionSelectableProfileCatalog.AdminOnlyProductionProfileId,
            selectedProfileDevOnly: false,
            governanceMode: ElectionGovernanceMode.AdminOnly,
            disclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            participationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            voteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            eligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            eligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            outcomeRule: CreatePassFailRule(),
            approvedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushsocial", "1.0.0"),
            ],
            protocolOmegaVersion: "omega-v1.0.0",
            reportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            reviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
            ownerOptions:
            [
                new ElectionOptionDefinition("yes", "Yes", "Approve the proposal", 1, false),
                new ElectionOptionDefinition("no", "No", "Reject the proposal", 2, false),
            ],
            officialResultVisibilityPolicy: OfficialResultVisibilityPolicy.PublicPlaintext);

        return draftElection with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            ClosedAt = new DateTime(2026, 5, 4, 12, 0, 0, DateTimeKind.Utc),
            TallyReadyAt = new DateTime(2026, 5, 4, 12, 5, 0, DateTimeKind.Utc),
            FinalizedAt = new DateTime(2026, 5, 4, 12, 10, 0, DateTimeKind.Utc),
        };
    }

    private static ElectionReportPackageBuildRequest CreateReportBuildRequest(
        ElectionRecord election,
        ProtocolPackageBindingRecord? protocolPackageBinding)
    {
        var closeArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Close,
            election,
            recordedByPublicAddress: "owner-address",
            frozenEligibleVoterSetHash: new byte[] { 1, 2, 3 },
            acceptedBallotCount: 1,
            acceptedBallotSetHash: new byte[] { 4, 5, 6 },
            publishedBallotCount: 1,
            publishedBallotStreamHash: new byte[] { 7, 8, 9 },
            finalEncryptedTallyHash: new byte[] { 10, 11, 12 });
        var tallyReadyArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.TallyReady,
            election,
            recordedByPublicAddress: "owner-address",
            acceptedBallotCount: 1,
            acceptedBallotSetHash: new byte[] { 4, 5, 6 },
            publishedBallotCount: 1,
            publishedBallotStreamHash: new byte[] { 7, 8, 9 },
            finalEncryptedTallyHash: new byte[] { 10, 11, 12 });
        var finalizeArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Finalize,
            election,
            recordedByPublicAddress: "owner-address",
            acceptedBallotCount: 1,
            acceptedBallotSetHash: new byte[] { 4, 5, 6 },
            publishedBallotCount: 1,
            publishedBallotStreamHash: new byte[] { 7, 8, 9 },
            finalEncryptedTallyHash: new byte[] { 10, 11, 12 });
        var finalizedElection = election with
        {
            CloseArtifactId = closeArtifact.Id,
            TallyReadyArtifactId = tallyReadyArtifact.Id,
            FinalizeArtifactId = finalizeArtifact.Id,
        };
        var denominatorEvidence = new ElectionResultDenominatorEvidence(
            ElectionEligibilitySnapshotType.Close,
            EligibilitySnapshotId: null,
            BoundaryArtifactId: closeArtifact.Id,
            ActiveDenominatorSetHash: new byte[] { 13, 14, 15 });
        var unofficialResult = ElectionModelFactory.CreateResultArtifact(
            election.ElectionId,
            ElectionResultArtifactKind.Unofficial,
            ElectionResultArtifactVisibility.PublicPlaintext,
            title: "Unofficial result",
            namedOptionResults:
            [
                new ElectionResultOptionCount("yes", "Yes", "Approve the proposal", 1, 1, 1),
                new ElectionResultOptionCount("no", "No", "Reject the proposal", 2, 2, 0),
            ],
            blankCount: 0,
            totalVotedCount: 1,
            eligibleToVoteCount: 1,
            didNotVoteCount: 0,
            denominatorEvidence,
            recordedByPublicAddress: "owner-address",
            tallyReadyArtifactId: tallyReadyArtifact.Id,
            publicPayload: "{\"mode\":\"protocol-package-report\"}");
        var officialResult = ElectionModelFactory.CreateResultArtifact(
            election.ElectionId,
            ElectionResultArtifactKind.Official,
            ElectionResultArtifactVisibility.PublicPlaintext,
            title: "Official result",
            namedOptionResults: unofficialResult.NamedOptionResults,
            blankCount: unofficialResult.BlankCount,
            totalVotedCount: unofficialResult.TotalVotedCount,
            eligibleToVoteCount: unofficialResult.EligibleToVoteCount,
            didNotVoteCount: unofficialResult.DidNotVoteCount,
            denominatorEvidence,
            recordedByPublicAddress: "owner-address",
            tallyReadyArtifactId: tallyReadyArtifact.Id,
            sourceResultArtifactId: unofficialResult.Id,
            publicPayload: "{\"mode\":\"protocol-package-report\"}");

        return new ElectionReportPackageBuildRequest(
            finalizedElection with
            {
                UnofficialResultArtifactId = unofficialResult.Id,
                OfficialResultArtifactId = officialResult.Id,
            },
            closeArtifact,
            tallyReadyArtifact,
            finalizeArtifact,
            unofficialResult,
            officialResult,
            CloseEligibilitySnapshot: null,
            ProtocolPackageBinding: protocolPackageBinding,
            FinalizationSession: null,
            FinalizationReleaseEvidence: null,
            FinalizationGovernedProposal: null,
            FinalizationGovernedApprovals: Array.Empty<ElectionGovernedProposalApprovalRecord>(),
            FinalizationShares: Array.Empty<ElectionFinalizationShareRecord>(),
            WarningAcknowledgements: Array.Empty<ElectionWarningAcknowledgementRecord>(),
            TrusteeInvitations: Array.Empty<ElectionTrusteeInvitationRecord>(),
            RosterEntries:
            [
                ElectionModelFactory.CreateRosterEntry(
                    election.ElectionId,
                    "voter-alice",
                    ElectionRosterContactType.Email,
                    "alice@hush.test"),
            ],
            ParticipationRecords:
            [
                ElectionModelFactory.CreateParticipationRecord(
                    election.ElectionId,
                    "voter-alice",
                    ElectionParticipationStatus.CountedAsVoted),
            ],
            AttemptNumber: 1,
            PreviousAttemptId: null,
            AttemptedByPublicAddress: "owner-address",
            AttemptedAt: new DateTime(2026, 5, 4, 12, 11, 0, DateTimeKind.Utc));
    }

    private static ProtocolPackageBindingRecord CreateSealedProtocolPackageBinding(ElectionRecord election)
    {
        var catalogEntry = ElectionModelFactory.CreateApprovedProtocolPackageCatalogEntry(
            packageId: "omega-hushvoting-v1",
            packageVersion: "v1.0.0",
            specPackageHash: Hash('a'),
            proofPackageHash: Hash('b'),
            releaseManifestHash: Hash('c'),
            compatibleProfileIds:
            [
                election.SelectedProfileId,
            ],
            approvalStatus: ProtocolPackageApprovalStatus.ApprovedInternal,
            isLatestForCompatibleProfiles: true,
            specAccessLocations:
            [
                ElectionModelFactory.CreateProtocolPackageAccessLocation(
                    ProtocolPackageAccessLocationKind.PublicWebsite,
                    "Website spec package",
                    "https://www.hushnetwork.social/protocol-omega/hushvoting-v1/spec.zip",
                    Hash('d')),
            ],
            proofAccessLocations:
            [
                ElectionModelFactory.CreateProtocolPackageAccessLocation(
                    ProtocolPackageAccessLocationKind.PublicWebsite,
                    "Website proof package",
                    "https://www.hushnetwork.social/protocol-omega/hushvoting-v1/proof.zip",
                    Hash('e')),
            ],
            approvedAt: new DateTime(2026, 5, 4, 11, 30, 0, DateTimeKind.Utc));

        var binding = ElectionModelFactory.CreateProtocolPackageBindingFromCatalog(
            election.ElectionId,
            catalogEntry,
            election.SelectedProfileId,
            election.CurrentDraftRevision,
            election.OwnerPublicAddress,
            boundAt: new DateTime(2026, 5, 4, 11, 45, 0, DateTimeKind.Utc));

        return binding.SealAtOpen(
            new DateTime(2026, 5, 4, 12, 0, 0, DateTimeKind.Utc),
            election.OwnerPublicAddress,
            sourceTransactionId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            sourceBlockHeight: 42,
            sourceBlockId: Guid.Parse("22222222-2222-2222-2222-222222222222"));
    }

    private static string Hash(char value) =>
        new(char.ToLowerInvariant(value), 64);
}
